using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Optimizer.WinUI.Services;

public class BackgroundMonitorService : IHostedService, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private readonly IDiskHealthService _diskHealth;
    private readonly INotificationService _notifications;
    private DispatcherTimer? _timer;
    private readonly Queue<double> _cpuHistory = new();

    public BackgroundMonitorService(
        ISystemMonitorService monitor,
        IDiskHealthService diskHealth,
        INotificationService notifications)
    {
        _monitor = monitor;
        _diskHealth = diskHealth;
        _notifications = notifications;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Stop();

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task CheckAsync()
    {
        try
        {
            // ── CPU sustained high ────────────────────────────────────────────
            var snapshot = _monitor.CollectSnapshot();
            _cpuHistory.Enqueue(snapshot.CpuUsagePercentage);
            while (_cpuHistory.Count > 4) _cpuHistory.Dequeue(); // last 2 min (30s × 4)

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
