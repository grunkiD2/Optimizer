using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Services.Events;

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
    private readonly IEventBus _eventBus;
    private readonly IFancontrolStatusService? _fancontrol;
    private readonly IUrgentAlertEgress? _urgentEgress;

    private readonly Queue<double> _cpuHistory = new();
    private int _tickCount;
    private bool _subscribed;

    public BackgroundMonitorService(
        ISystemDataBus dataBus,
        IDiskHealthService diskHealth,
        INotificationService notifications,
        IEventBus eventBus,
        IFancontrolStatusService? fancontrol = null,
        IUrgentAlertEgress? urgentEgress = null)
    {
        _dataBus       = dataBus;
        _diskHealth    = diskHealth;
        _notifications = notifications;
        _eventBus      = eventBus;
        _fancontrol    = fancontrol;
        _urgentEgress  = urgentEgress;
    }

    /// <summary>R5: urgent findings also go to the phone (ntfy); failures must never break monitoring.</summary>
    private async Task PushUrgentAsync(string title, string detail)
    {
        if (_urgentEgress == null) return;
        try { await _urgentEgress.PushUrgentAsync(title, detail); }
        catch (Exception ex) { EngineLog.Error("Urgent egress push failed", ex); }
    }

    /// <summary>
    /// Alert dedup on federated machines (docs/MACHINE-OWNERSHIP.md): the fan brain's alarm
    /// layer + sentinel + ntfy own thermal alerting. Optimizer only steps in as a FAILSAFE
    /// when the brain is dead/stale — otherwise both systems would alert on the same event.
    /// </summary>
    private bool FancontrolOwnsThermal()
    {
        try { return _fancontrol?.IsConfigured == true && _fancontrol.GetStatus()?.Brain is { Stale: false }; }
        catch { return false; }
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
                var title = "High CPU usage detected";
                var detail = $"CPU has been at {snapshot.CpuUsagePercentage:F0}% for over 2 minutes.";
                _notifications.Show(title, detail, NotificationCategory.Performance);
                _eventBus.Publish(OptimizerEvent.Create(
                    OptimizerEventType.ThresholdCrossed, title, detail,
                    new Dictionary<string, string>
                    {
                        ["metric"] = "CpuUsage",
                        ["value"]  = snapshot.CpuUsagePercentage.ToString("F0")
                    }));
            }

            // ── CPU thermal warning (suppressed when the Fancontrol brain owns thermal) ──
            if (snapshot.CpuTemperature > 90 && !FancontrolOwnsThermal())
            {
                var title = "CPU thermal warning";
                var detail = $"CPU temperature is {snapshot.CpuTemperature:F0}°C. Check cooling immediately.";
                _notifications.Show(title, detail, NotificationCategory.Hardware);
                // R5: this only fires as the FAILSAFE (brain dead/stale + >90 °C) — that exact
                // combination is the most urgent state this machine has. Phone, not just toast.
                await PushUrgentAsync(title, detail + " (Fancontrol brain is dead/stale — failsafe alert.)");
                _eventBus.Publish(OptimizerEvent.Create(
                    OptimizerEventType.ThresholdCrossed, title, detail,
                    new Dictionary<string, string>
                    {
                        ["metric"] = "CpuTemperature",
                        ["value"]  = snapshot.CpuTemperature.ToString("F0")
                    }));
            }

            // ── Disk space ────────────────────────────────────────────────────
            foreach (var drive in DriveInfo.GetDrives()
                         .Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usedPct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                if (usedPct > 95)
                {
                    var freeGb = drive.AvailableFreeSpace / 1_073_741_824L;
                    var title = "Drive nearly full";
                    var detail = $"{drive.Name} is {usedPct:F0}% full. Only {freeGb} GB remaining.";
                    _notifications.Show(title, detail, NotificationCategory.Storage);
                    _eventBus.Publish(OptimizerEvent.Create(
                        OptimizerEventType.ThresholdCrossed, title, detail,
                        new Dictionary<string, string>
                        {
                            ["metric"] = "DiskUsage",
                            ["drive"]  = drive.Name,
                            ["value"]  = usedPct.ToString("F0")
                        }));
                }
            }

            // ── SMART warnings ────────────────────────────────────────────────
            var disks = await _diskHealth.GetDiskHealthAsync();
            foreach (var disk in disks.Where(d => d.IsPredictedToFail))
            {
                var title = "Drive failure predicted";
                var detail = $"{disk.Model} reports unhealthy SMART status. Back up your data immediately.";
                _notifications.Show(title, detail, NotificationCategory.Hardware);
                await PushUrgentAsync(title, detail);   // R5: SMART death-rattle goes to the phone
                _eventBus.Publish(OptimizerEvent.Create(
                    OptimizerEventType.ThresholdCrossed, title, detail,
                    new Dictionary<string, string>
                    {
                        ["metric"] = "SmartHealth",
                        ["disk"]   = disk.Model
                    }));
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Background monitor check failed", ex);
        }
    }
}
