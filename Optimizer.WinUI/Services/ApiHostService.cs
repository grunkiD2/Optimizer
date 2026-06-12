using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

        // Serve static files from WebDashboard folder (co-located with the exe)
        var webRoot = Path.Combine(AppContext.BaseDirectory, "WebDashboard");
        if (Directory.Exists(webRoot))
        {
            var fileProvider = new PhysicalFileProvider(webRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider, RequestPath = "" });

            // Explicit MIME types for PWA manifests and service workers
            var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            contentTypes.Mappings[".json"]        = "application/json";
            contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
            // manifest.json served with manifest MIME type via exact-name match below
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider,
                RequestPath  = "",
                ContentTypeProvider = contentTypes,
                OnPrepareResponse = ctx =>
                {
                    // Override Content-Type for manifest.json specifically
                    if (ctx.File.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Context.Response.ContentType = "application/manifest+json; charset=utf-8";
                    }
                    // Service worker must be served from root scope, allow caching control
                    if (ctx.File.Name.Equals("service-worker.js", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Context.Response.Headers["Service-Worker-Allowed"] = "/";
                        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
                    }
                }
            });
        }

        // Bearer auth middleware — only applies to /api/* routes
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
            {
                var auth = context.Request.Headers["Authorization"].ToString();
                if (!string.Equals(auth, $"Bearer {token}", StringComparison.Ordinal))
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
}

/// <summary>Request item for POST /api/apply/batch.</summary>
public sealed class BatchApplyItem
{
    /// <summary>"profile" or "optimization".</summary>
    public string Type { get; set; } = "profile";
    public string Id { get; set; } = "";
}
