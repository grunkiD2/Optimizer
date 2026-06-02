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
        if (active)
        {
            _sensorTimer ??= new System.Threading.Timer(OnSensorTick, null, Timeout.Infinite, Timeout.Infinite);
            _sensorTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
        else
        {
            _sensorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
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
    }
}
