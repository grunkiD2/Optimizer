using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ApiHostService : IApiHostService
{
    private WebApplication? _app;
    private readonly IServiceProvider _appServices;

    public bool IsRunning => _app != null;
    public string ListeningUrl { get; private set; } = "";

    public ApiHostService(IServiceProvider services)
    {
        _appServices = services;
    }

    public async Task StartAsync(int port, string token)
    {
        if (_app != null) return;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        // Bind to localhost only for security
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // OpenAPI / Swagger — .NET 9+ built-in (no Swashbuckle needed)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Serve /openapi/v1.json
        app.MapOpenApi();

        // (The phone PWA / WebDashboard was removed 2026-06-12 — the API serves the CLI,
        // the assistant tool surface, and local automation only. VISION.md: local-only.)

        // Bearer auth middleware — only applies to /api/* routes
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
            {
                var auth = context.Request.Headers["Authorization"].ToString();
                if (!TokenMatches(auth, token))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }
            }
            await next();
        });

        // ── Endpoints ──────────────────────────────────────────────────────────

        app.MapGet("/api/health", () =>
            Results.Ok(new { status = "ok", version = "2.0", timestamp = DateTime.UtcNow }))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithOpenApi();

        app.MapGet("/api/metrics", () =>
        {
            var monitor = _appServices.GetService<ISystemMonitorService>();
            if (monitor == null) return Results.StatusCode(503);
            var snap = monitor.CollectSnapshot();
            return Results.Ok(new
            {
                cpu    = snap.CpuUsagePercentage,
                memory = new
                {
                    total     = snap.TotalPhysicalMemory,
                    available = snap.AvailablePhysicalMemory
                },
                gpu  = snap.GpuUsagePercentage,
                disk = new
                {
                    readSpeed  = snap.DiskReadSpeed,
                    writeSpeed = snap.DiskWriteSpeed
                },
                network = new
                {
                    inSpeed  = snap.NetworkInSpeed,
                    outSpeed = snap.NetworkOutSpeed
                },
                timestamp = snap.Timestamp
            });
        })
        .WithName("GetMetrics")
        .WithTags("System")
        .WithOpenApi();

        app.MapGet("/api/sensors", () =>
        {
            var sensors = _appServices.GetService<ISensorService>();
            if (sensors == null || !sensors.IsAvailable) return Results.StatusCode(503);
            var snap = sensors.GetSnapshot();
            return Results.Ok(new
            {
                cpuTemp   = snap.CpuPackageTemperatureC,
                gpuTemp   = snap.GpuTemperatureC,
                cpuPower  = snap.CpuPowerWatts,
                gpuPower  = snap.GpuPowerWatts,
                gpuCoreMhz = snap.GpuCoreMhz,
                gpuVramUsedMb = snap.GpuMemoryUsedMb,
                fans      = snap.FanSpeeds.Select(f => new { name = f.Name, rpm = f.Value })
            });
        })
        .WithName("GetSensors")
        .WithTags("Sensors")
        .WithOpenApi();

        app.MapGet("/api/fancontrol", () =>
        {
            var fancontrol = _appServices.GetService<IFancontrolStatusService>();
            if (fancontrol == null || !fancontrol.IsConfigured)
                return Results.NotFound(new { error = "Fancontrol federation not configured (AppSettings.FancontrolStateDir)" });
            var status = fancontrol.GetStatus();
            return status == null ? Results.StatusCode(503) : Results.Ok(status);
        })
        .WithName("GetFancontrolStatus")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapGet("/api/fancontrol/history", async (double? hours, int? maxPoints, CancellationToken ct) =>
        {
            var telemetry = _appServices.GetService<IFancontrolTelemetryService>();
            if (telemetry == null)
                return Results.NotFound(new { error = "Fancontrol federation not configured" });
            var points = await telemetry.GetHistoryAsync(hours ?? 24, maxPoints ?? 300, ct);
            return Results.Ok(new { count = points.Count, points });
        })
        .WithName("GetFancontrolHistory")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapGet("/api/power/processes", () =>
        {
            var ppi = _appServices.GetService<Optimizer.WinUI.Services.Power.IPowerInsightsService>();
            if (ppi == null || !ppi.Enabled)
                return Results.NotFound(new { error = "Power Insights disabled (AppSettings.PpiEnabled)" });
            var snap = ppi.LatestSnapshot;
            return Results.Ok(new
            {
                // R7: the first REVERSE contract (C# producer → PS consumer). The Fancontrol
                // brain's future attribution-bias consumer pins this version; bump it on ANY
                // field rename/semantic change so the PS side can fail closed instead of
                // silently reading nulls (the same rule the state-file contracts follow).
                schemaVersion = 1,
                timestamp = snap?.Timestamp,
                windowSeconds = snap?.WindowSeconds,
                packageWatts = snap?.PackageWatts,
                attributedShare = snap?.AttributedShare,
                context = ppi.LatestContext,
                model = "estimated: cpu-time share × measured package watts",
                processes = ppi.GetTopDrainers(),
            });
        })
        .WithName("GetPowerProcesses")
        .WithTags("Power")
        .WithOpenApi();

        app.MapGet("/api/power/drift", async (double? hours, int? limit) =>
        {
            var ppi = _appServices.GetService<Optimizer.WinUI.Services.Power.IPowerInsightsService>();
            if (ppi == null || !ppi.Enabled)
                return Results.NotFound(new { error = "Power Insights disabled (AppSettings.PpiEnabled)" });
            var events = await ppi.GetRecentDriftAsync(hours ?? 24, limit ?? 50);
            return Results.Ok(new { schemaVersion = 1, count = events.Count, events });   // R7: reverse contract version
        })
        .WithName("GetPowerDrift")
        .WithTags("Power")
        .WithOpenApi();

        app.MapGet("/api/fancontrol/profiles", () =>
        {
            var fc = _appServices.GetService<IFancontrolCommandService>();
            if (fc == null || !fc.IsConfigured)
                return Results.NotFound(new { error = "Fancontrol federation not configured" });
            return Results.Ok(fc.GetProfileNames());
        })
        .WithName("GetFancontrolProfiles")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapPost("/api/fancontrol/apply-profile", async (FancontrolProfileRequest req, CancellationToken ct) =>
        {
            var fc = _appServices.GetService<IFancontrolCommandService>();
            if (fc == null || !fc.IsConfigured)
                return Results.NotFound(new { error = "Fancontrol federation not configured" });
            var r = await fc.ApplyProfileAsync(req.Profile ?? "", ct);
            return Results.Ok(new { success = r.Success, output = r.Output });
        })
        .WithName("FancontrolApplyProfile")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapPost("/api/fancontrol/night", async (FancontrolNightRequest req, CancellationToken ct) =>
        {
            var fc = _appServices.GetService<IFancontrolCommandService>();
            if (fc == null || !fc.IsConfigured)
                return Results.NotFound(new { error = "Fancontrol federation not configured" });
            var r = await fc.SetNightAsync(req.Mode ?? "", ct);
            return Results.Ok(new { success = r.Success, output = r.Output });
        })
        .WithName("FancontrolNight")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapPost("/api/fancontrol/ack-alerts", async (FancontrolAckRequest req, CancellationToken ct) =>
        {
            var fc = _appServices.GetService<IFancontrolCommandService>();
            if (fc == null || !fc.IsConfigured)
                return Results.NotFound(new { error = "Fancontrol federation not configured" });
            var r = await fc.AckAlertsAsync(req.Note, ct);
            return Results.Ok(new { success = r.Success, output = r.Output });
        })
        .WithName("FancontrolAckAlerts")
        .WithTags("Fancontrol")
        .WithOpenApi();

        app.MapGet("/api/profiles", () =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var presets = optimizer.GetBuiltInPresets();
            return Results.Ok(presets.Select(p => new
            {
                id          = p.Id,
                name        = p.Name,
                description = p.Description
            }));
        })
        .WithName("GetProfiles")
        .WithTags("Profiles")
        .WithOpenApi();

        app.MapPost("/api/apply/{profileId}", async (string profileId) =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var success = await optimizer.ApplyProfileAsync(profileId);
            return Results.Ok(new { success, profileId });
        })
        .WithName("ApplyProfile")
        .WithTags("Profiles")
        .WithOpenApi();

        app.MapPost("/api/cleanup", async () =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var result = await optimizer.ApplyOptimizationAsync(OptimizationIds.ClearTemporaryFiles);
            return Results.Ok(new { success = result.Success, message = result.Message });
        })
        .WithName("RunCleanup")
        .WithTags("System")
        .WithOpenApi();

        app.MapGet("/api/hardware", async () =>
        {
            var hw = _appServices.GetService<IHardwareInfoService>();
            if (hw == null) return Results.StatusCode(503);
            return Results.Ok(await hw.GetHardwareInfoAsync());
        })
        .WithName("GetHardware")
        .WithTags("Hardware")
        .WithOpenApi();

        app.MapGet("/api/disks", async () =>
        {
            var dh = _appServices.GetService<IDiskHealthService>();
            if (dh == null) return Results.StatusCode(503);
            return Results.Ok(await dh.GetDiskHealthAsync());
        })
        .WithName("GetDiskHealth")
        .WithTags("Hardware")
        .WithOpenApi();

        app.MapGet("/api/recommendations", async () =>
        {
            var recs = _appServices.GetService<IRecommendationsService>();
            if (recs == null) return Results.StatusCode(503);
            var generated = await recs.GenerateAsync();
            // Project to a DTO: the Recommendation model carries a QuickAction delegate
            // (Func<Task<bool>>) that System.Text.Json cannot serialize — returning the raw
            // model 500s whenever a recommendation with a QuickAction is present.
            return Results.Ok(generated.Select(r => new
            {
                id          = r.Id,
                title       = r.Title,
                description = r.Description,
                actionLabel = r.ActionLabel,
                severity    = r.Severity.ToString(),
                category    = r.Category.ToString(),
                mlConfidence = r.MlConfidence
            }));
        })
        .WithName("GetRecommendations")
        .WithTags("Diagnostics")
        .WithOpenApi();

        // ── Phase 5: list all optimization ids (not just profiles) ──────────────
        app.MapGet("/api/optimizations", async () =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var ids = await optimizer.GetAvailableOptimizationsAsync();
            return Results.Ok(ids.Select(id =>
            {
                var info = optimizer.GetOptimizationInfo(id);
                return new { id, title = info?.Title ?? id, summary = info?.Summary ?? "" };
            }));
        })
        .WithName("GetOptimizations")
        .WithTags("Optimizations")
        .WithOpenApi();

        // ── Phase 5: profile export / import ────────────────────────────────────
        app.MapGet("/api/profiles/export", (HttpContext ctx) =>
        {
            var profiles = _appServices.GetService<IProfileService>();
            if (profiles == null) return Results.StatusCode(503);
            var json = profiles.ExportAll();
            return Results.Content(json, "application/json");
        })
        .WithName("ExportProfiles")
        .WithTags("Profiles")
        .WithOpenApi();

        app.MapPost("/api/profiles/import", async (HttpContext ctx) =>
        {
            var profiles = _appServices.GetService<IProfileService>();
            if (profiles == null) return Results.StatusCode(503);

            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(json))
                return Results.BadRequest(new { error = "Empty body." });

            try
            {
                profiles.ImportFromJson(json);
                return Results.Ok(new { imported = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ImportProfiles")
        .WithTags("Profiles")
        .WithOpenApi();

        // ── Phase 5: batch apply ────────────────────────────────────────────────
        app.MapPost("/api/apply/batch", async (List<BatchApplyItem> items) =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            if (items.Count == 0) return Results.BadRequest(new { error = "No items." });

            var results = new List<object>();
            foreach (var item in items)
            {
                bool success;
                string reason;
                try
                {
                    if (string.Equals(item.Type, "optimization", StringComparison.OrdinalIgnoreCase))
                    {
                        var r = await optimizer.ApplyOptimizationAsync(item.Id);
                        success = r.Success;
                        reason = r.Message;
                    }
                    else
                    {
                        success = await optimizer.ApplyProfileAsync(item.Id);
                        reason = success ? "applied" : "completed with errors";
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    reason = ex.Message;
                }
                results.Add(new { id = item.Id, type = item.Type, success, reason });
            }
            return Results.Ok(results);
        })
        .WithName("ApplyBatch")
        .WithTags("Optimizations")
        .WithOpenApi();

        // ── Phase 5: schedules ──────────────────────────────────────────────────
        app.MapGet("/api/schedules", async () =>
        {
            var scheduler = _appServices.GetService<IScheduledOptimizationService>();
            if (scheduler == null) return Results.StatusCode(503);
            return Results.Ok(await scheduler.GetAllAsync());
        })
        .WithName("GetSchedules")
        .WithTags("Schedules")
        .WithOpenApi();

        app.MapPost("/api/schedules", async (ScheduledTask task) =>
        {
            var scheduler = _appServices.GetService<IScheduledOptimizationService>();
            if (scheduler == null) return Results.StatusCode(503);
            if (string.IsNullOrWhiteSpace(task.TargetId))
                return Results.BadRequest(new { error = "TargetId is required." });
            var created = await scheduler.CreateAsync(task);
            return Results.Ok(created);
        })
        .WithName("CreateSchedule")
        .WithTags("Schedules")
        .WithOpenApi();

        app.MapDelete("/api/schedules/{id}", async (string id) =>
        {
            var scheduler = _appServices.GetService<IScheduledOptimizationService>();
            if (scheduler == null) return Results.StatusCode(503);
            var ok = await scheduler.DeleteAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound();
        })
        .WithName("DeleteSchedule")
        .WithTags("Schedules")
        .WithOpenApi();

        _app = app;
        await _app.StartAsync();
        ListeningUrl = $"http://localhost:{port}";
        EngineLog.Write($"API server started at {ListeningUrl}");
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        ListeningUrl = "";
        EngineLog.Write("API server stopped");
    }

    /// <summary>
    /// Constant-time bearer check (CryptographicOperations.FixedTimeEquals) — plain string
    /// comparison leaks the match length through timing. Internal for tests.
    /// </summary>
    internal static bool TokenMatches(string authorizationHeader, string token)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorizationHeader) || string.IsNullOrEmpty(token)) return false;
        if (!authorizationHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = System.Text.Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]);
        var expected = System.Text.Encoding.UTF8.GetBytes(token);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>Body for POST /api/fancontrol/apply-profile.</summary>
public sealed record FancontrolProfileRequest(string? Profile);

/// <summary>Body for POST /api/fancontrol/night.</summary>
public sealed record FancontrolNightRequest(string? Mode);

/// <summary>Body for POST /api/fancontrol/ack-alerts.</summary>
public sealed record FancontrolAckRequest(string? Note);

/// <summary>Request item for POST /api/apply/batch.</summary>
public sealed class BatchApplyItem
{
    /// <summary>"profile" or "optimization".</summary>
    public string Type { get; set; } = "profile";
    public string Id { get; set; } = "";
}
