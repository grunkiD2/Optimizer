using Microsoft.Extensions.Hosting;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Subscribes to the SystemDataBus metrics stream and fires alerts at 30s cadence
/// (every 30th reading at 1 Hz = ~30 s).
/// </summary>
public class BackgroundMonitorService : IHostedService, IDisposable
{
    private readonly ISystemDataBus _dataBus;
    private readonly IDiskHealthService _diskHealth;
    private readonly INotificationService _notifications;

    private readonly Queue<double> _cpuHistory = new();
    private int _tickCount;
    private bool _subscribed;

    public BackgroundMonitorService(
        ISystemDataBus dataBus,
        IDiskHealthService diskHealth,
        INotificationService notifications)
    {
        _dataBus       = dataBus;
        _diskHealth    = diskHealth;
        _notifications = notifications;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscribed) return Task.CompletedTask;
        _subscribed = true;
        _dataBus.MetricsUpdated += OnMetricsUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed) return Task.CompletedTask;
        _subscribed = false;
        _dataBus.MetricsUpdated -= OnMetricsUpdated;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            _subscribed = false;
            _dataBus.MetricsUpdated -= OnMetricsUpdated;
        }
        GC.SuppressFinalize(this);
    }

    // ── Metrics handler ───────────────────────────────────────────────────────

    private void OnMetricsUpdated(Models.SystemResource snapshot)
    {
        // Throttle: run full check every 30 ticks (≈30 s at 1 Hz bus cadence).
        _tickCount++;
        if (_tickCount < 30) return;
        _tickCount = 0;

        // Fire-and-forget; errors are logged internally.
        _ = CheckAsync(snapshot);
    }

    private async Task CheckAsync(Models.SystemResource snapshot)
    {
        try
        {
            // ── CPU sustained high ────────────────────────────────────────────
            _cpuHistory.Enqueue(snapshot.CpuUsagePercentage);
            while (_cpuHistory.Count > 4) _cpuHistory.Dequeue(); // last ~2 min (30s × 4)

            if (_cpuHistory.Count >= 4 && _cpuHistory.All(c => c > 90))
            {
                _notifications.Show(
                    "High CPU usage detected",
                    $"CPU has been at {snapshot.CpuUsagePercentage:F0}% for over 2 minutes.",
                    NotificationCategory.Performance);
            }

            // ── CPU thermal warning ───────────────────────────────────────────
            if (snapshot.CpuTemperature > 90)
            {
                _notifications.Show(
                    "CPU thermal warning",
                    $"CPU temperature is {snapshot.CpuTemperature:F0}°C. Check cooling immediately.",
                    NotificationCategory.Hardware);
            }

            // ── Disk space ────────────────────────────────────────────────────
            foreach (var drive in DriveInfo.GetDrives()
                         .Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usedPct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                if (usedPct > 95)
                {
                    var freeGb = drive.AvailableFreeSpace / 1_073_741_824L;
                    _notifications.Show(
                        "Drive nearly full",
                        $"{drive.Name} is {usedPct:F0}% full. Only {freeGb} GB remaining.",
                        NotificationCategory.Storage);
                }
            }

            // ── SMART warnings ────────────────────────────────────────────────
            var disks = await _diskHealth.GetDiskHealthAsync();
            foreach (var disk in disks.Where(d => d.IsPredictedToFail))
            {
                _notifications.Show(
                    "Drive failure predicted",
                    $"{disk.Model} reports unhealthy SMART status. Back up your data immediately.",
                    NotificationCategory.Hardware);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Background monitor check failed", ex);
        }
    }
}
