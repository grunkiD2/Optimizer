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

        var app = builder.Build();

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
            if (context.Request.Path.StartsWithSegments("/api"))
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
            Results.Ok(new { status = "ok", version = "2.0", timestamp = DateTime.UtcNow }));

        app.MapGet("/api/metrics", () =>
        {
            var monitor = _appServices.GetService<SystemMonitorService>();
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
        });

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
                fans      = snap.FanSpeeds.Select(f => new { name = f.Name, rpm = f.Value })
            });
        });

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
        });

        app.MapPost("/api/apply/{profileId}", async (string profileId) =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var success = await optimizer.ApplyProfileAsync(profileId);
            return Results.Ok(new { success, profileId });
        });

        app.MapPost("/api/cleanup", async () =>
        {
            var optimizer = _appServices.GetService<IWindowsOptimizerService>();
            if (optimizer == null) return Results.StatusCode(503);
            var result = await optimizer.ApplyOptimizationAsync(OptimizationIds.ClearTemporaryFiles);
            return Results.Ok(new { success = result.Success, message = result.Message });
        });

        app.MapGet("/api/hardware", async () =>
        {
            var hw = _appServices.GetService<IHardwareInfoService>();
            if (hw == null) return Results.StatusCode(503);
            return Results.Ok(await hw.GetHardwareInfoAsync());
        });

        app.MapGet("/api/disks", async () =>
        {
            var dh = _appServices.GetService<IDiskHealthService>();
            if (dh == null) return Results.StatusCode(503);
            return Results.Ok(await dh.GetDiskHealthAsync());
        });

        app.MapGet("/api/recommendations", async () =>
        {
            var recs = _appServices.GetService<IRecommendationsService>();
            if (recs == null) return Results.StatusCode(503);
            return Results.Ok(await recs.GenerateAsync());
        });

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
