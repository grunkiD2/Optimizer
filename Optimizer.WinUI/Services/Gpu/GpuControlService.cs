using Optimizer.WinUI.Models;
using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

public sealed class GpuControlService : IGpuControlService
{
    private readonly ISensorService _sensors;
    private readonly IGpuControlBackend _activeBackend;

    public GpuVendor PrimaryVendor => _activeBackend.Vendor;
    public bool OcWriteAvailable => _activeBackend.IsAvailable;
    public string? OcUnavailableReason => _activeBackend.UnavailableReason;
    public GpuControlCapabilities Capabilities => _activeBackend.GetCapabilities();

    // ── Constructor: pick the first available real backend, or fall back to Null ─

    public GpuControlService(
        ISensorService sensors,
        IEnumerable<IGpuControlBackend> backends)
    {
        _sensors = sensors;
        // First backend that reports IsAvailable wins; NullGpuBackend is always last.
        _activeBackend = backends.FirstOrDefault(b => b.IsAvailable)
                      ?? new NullGpuBackend();
    }

    // ── Telemetry — always via LHM/SensorService ──────────────────────────────

    public IReadOnlyList<GpuTelemetrySnapshot> ReadTelemetry()
    {
        if (!_sensors.IsAvailable) return [];

        try
        {
            var snap = _sensors.GetSnapshot();
            return MapSnapshot(snap);
        }
        catch (Exception ex)
        {
            EngineLog.Error("GpuControlService.ReadTelemetry failed", ex);
            return [];
        }
    }

    private List<GpuTelemetrySnapshot> MapSnapshot(HardwareSnapshot snap)
    {
        // LibreHardwareMonitor reports all GPU sensors together (not per-GPU),
        // so we produce one composite snapshot reflecting the primary GPU.
        var result = new GpuTelemetrySnapshot
        {
            Name        = snap.GpuTemperatures.FirstOrDefault()?.HardwareName
                       ?? snap.GpuClocks.FirstOrDefault()?.HardwareName
                       ?? "GPU",
            Vendor      = PrimaryVendor,
            CoreClockMhz = snap.GpuCoreMhz,
            MemoryClockMhz = snap.GpuMemoryMhz,
            TemperatureC = snap.GpuTemperatureC,
            PowerWatts   = snap.GpuPowerWatts,
            FanRpm       = snap.FanSpeeds.FirstOrDefault()?.Value,
            FanPercent   = null,   // LHM reports fan RPM; % not always available
            LoadPercent  = snap.GpuLoads.FirstOrDefault(s =>
                               s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))?.Value
                        ?? snap.GpuLoads.FirstOrDefault()?.Value,
            VramUsedMb   = snap.GpuMemoryUsedMb,
        };

        // Only return a snapshot if we actually have some data
        if (result.CoreClockMhz == null && result.TemperatureC == null &&
            result.PowerWatts == null && result.LoadPercent == null)
        {
            return [];
        }

        return [result];
    }

    // ── Apply OC ──────────────────────────────────────────────────────────────

    public (bool ok, string error, GpuControlState applied) Apply(GpuControlState desired)
    {
        var caps    = Capabilities;
        var clamped = Clamp(desired, caps);

        if (!_activeBackend.IsAvailable)
        {
            var reason = _activeBackend.UnavailableReason ?? "No GPU backend available.";
            return (false, reason, clamped);
        }

        var ok = _activeBackend.TryApply(clamped, out var error);
        return (ok, error, clamped);
    }

    public void ResetToDefault()
    {
        try { _activeBackend.ResetToDefault(); }
        catch (Exception ex) { EngineLog.Error("GpuControlService.ResetToDefault failed", ex); }
    }

    // ── Watchdog stress test ──────────────────────────────────────────────────

    public async Task<string> ApplyWithWatchdogAsync(
        GpuControlState desired,
        int watchdogTempC,
        TimeSpan testDuration,
        CancellationToken ct)
    {
        var (ok, applyError, _) = Apply(desired);
        if (!ok)
            return $"Could not apply GPU settings: {applyError}";

        var clampedWatchdog = Math.Clamp(watchdogTempC, 60, 105);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            while (sw.Elapsed < testDuration && !ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);

                if (!_sensors.IsAvailable) continue;

                double? temp = null;
                try
                {
                    var snap = _sensors.GetSnapshot();
                    temp = snap.GpuTemperatureC;
                }
                catch { /* ignore transient sensor errors */ }

                if (temp.HasValue && temp.Value > clampedWatchdog)
                {
                    ResetToDefault();
                    return $"GPU watchdog aborted at {temp.Value:F0}°C (limit {clampedWatchdog}°C). Settings reset to default.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            ResetToDefault();
            return "GPU watchdog test cancelled. Settings reset to default.";
        }

        return $"GPU watchdog test completed ({testDuration.TotalSeconds:F0}s). No thermal limit exceeded.";
    }

    // ── Clamping logic ────────────────────────────────────────────────────────

    internal static GpuControlState Clamp(GpuControlState d, GpuControlCapabilities c) => new()
    {
        CoreClockOffsetMhz   = Math.Clamp(d.CoreClockOffsetMhz,   c.CoreOffsetRangeMhz.Min,     c.CoreOffsetRangeMhz.Max),
        MemoryClockOffsetMhz = Math.Clamp(d.MemoryClockOffsetMhz, c.MemoryOffsetRangeMhz.Min,   c.MemoryOffsetRangeMhz.Max),
        PowerLimitPercent    = Math.Clamp(d.PowerLimitPercent,     c.PowerLimitRangePercent.Min, c.PowerLimitRangePercent.Max),
        TempLimitC           = Math.Clamp(d.TempLimitC, 60, 95),
        FanPercent           = d.FanPercent.HasValue ? Math.Clamp(d.FanPercent.Value, 0, 100) : (int?)null,
    };
}
