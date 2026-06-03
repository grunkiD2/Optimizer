using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SystemDataBus : ISystemDataBus, IHostedService, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private readonly ISensorService _sensors;

    private readonly System.Threading.Timer _metricsTimer;
    private System.Threading.Timer? _sensorTimer;
    private System.Threading.Timer? _latencyTimer;
    private readonly System.Net.NetworkInformation.Ping _ping = new();

    private bool _sensorsActive;
    private bool _latencyActive;
    private bool _disposed;

    // Sensors (LibreHardwareMonitor) always sample at a slow background heartbeat so temperature
    // data is fresh for background thermal alerts and metric enrichment even with no UI open;
    // a page that shows live sensors bumps the cadence up via SetSensorsActive(true).
    private static readonly TimeSpan SensorHeartbeat = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SensorActiveInterval = TimeSpan.FromSeconds(2);

    public event Action<SystemResource>? MetricsUpdated;
    public event Action<HardwareSnapshot>? SensorsUpdated;
    public event Action<double>? LatencyUpdated;

    public SystemResource? LatestMetrics { get; private set; }
    public HardwareSnapshot? LatestSensors { get; private set; }

    public SystemDataBus(ISystemMonitorService monitor, ISensorService sensors)
    {
        _monitor = monitor;
        _sensors = sensors;
        _metricsTimer = new System.Threading.Timer(OnMetricsTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    // ── ISystemDataBus ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct = default)
    {
        _metricsTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        // Start the slow sensor heartbeat (only does work if LHM initialized successfully).
        _sensorTimer ??= new System.Threading.Timer(OnSensorTick, null, Timeout.Infinite, Timeout.Infinite);
        _sensorTimer.Change(TimeSpan.Zero, SensorHeartbeat);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _metricsTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _sensorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _latencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    Task IHostedService.StartAsync(CancellationToken ct) => StartAsync(ct);
    Task IHostedService.StopAsync(CancellationToken ct) => StopAsync();

    // ── Sensor / latency activation ───────────────────────────────────────────

    public void SetSensorsActive(bool active)
    {
        if (_sensorsActive == active) return;
        _sensorsActive = active;
        _sensorTimer ??= new System.Threading.Timer(OnSensorTick, null, Timeout.Infinite, Timeout.Infinite);
        // Active → fast cadence for live UI; inactive → fall back to the background heartbeat
        // (not fully off) so LatestSensors never goes stale.
        _sensorTimer.Change(TimeSpan.Zero, active ? SensorActiveInterval : SensorHeartbeat);
    }

    public void SetLatencyActive(bool active)
    {
        if (_latencyActive == active) return;
        _latencyActive = active;
        if (active)
        {
            _latencyTimer ??= new System.Threading.Timer(OnLatencyTick, null, Timeout.Infinite, Timeout.Infinite);
            _latencyTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        else
        {
            _latencyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    // ── Timer callbacks ───────────────────────────────────────────────────────

    private void OnMetricsTick(object? _)
    {
        try
        {
            var snap = _monitor.CollectSnapshot();
            // Enrich with temperatures from the latest LHM sample. SystemMonitorService no longer
            // reads temps (WMI Win32_TemperatureProbe is empty on consumer desktops); reading the
            // cached snapshot here keeps SystemDataBus the single LHM owner (no concurrent Update()).
            if (LatestSensors is { } sensors)
            {
                if (sensors.CpuPackageTemperatureC is { } cpuTemp) snap.CpuTemperature = cpuTemp;
                if (sensors.GpuTemperatureC is { } gpuTemp) snap.GpuTemperature = gpuTemp;
            }
            LatestMetrics = snap;
            MetricsUpdated?.Invoke(snap);
        }
        catch (Exception ex) { EngineLog.Error("Metrics tick failed", ex); }
    }

    private void OnSensorTick(object? _)
    {
        if (!_sensors.IsAvailable) return;
        try
        {
            var snap = _sensors.GetSnapshot();
            LatestSensors = snap;
            SensorsUpdated?.Invoke(snap);
        }
        catch (Exception ex) { EngineLog.Error("Sensor tick failed", ex); }
    }

    private async void OnLatencyTick(object? _)
    {
        try
        {
            var reply = await _ping.SendPingAsync("1.1.1.1", 1500);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                LatencyUpdated?.Invoke(reply.RoundtripTime);
        }
        catch (Exception ex) { EngineLog.Error("Latency tick failed", ex); }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _metricsTimer.Dispose();
        _sensorTimer?.Dispose();
        _latencyTimer?.Dispose();
        _ping.Dispose();
        GC.SuppressFinalize(this);
    }
}
