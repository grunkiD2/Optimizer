using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

public interface IGpuControlService
{
    /// <summary>
    /// Read live GPU telemetry via LibreHardwareMonitor (SensorService).
    /// Always works when LHM is available, regardless of OC write capability.
    /// </summary>
    IReadOnlyList<GpuTelemetrySnapshot> ReadTelemetry();

    GpuVendor PrimaryVendor { get; }

    /// <summary>True only when a real backend (NVAPI/ADL) can write OC settings.</summary>
    bool OcWriteAvailable { get; }

    /// <summary>Human-readable reason when OcWriteAvailable is false.</summary>
    string? OcUnavailableReason { get; }

    GpuControlCapabilities Capabilities { get; }

    /// <summary>
    /// Clamps <paramref name="desired"/> into Capabilities ranges, then applies
    /// via the active backend. Returns the clamped state actually sent.
    /// </summary>
    (bool ok, string error, GpuControlState applied) Apply(GpuControlState desired);

    /// <summary>Resets GPU to driver defaults (no-op when OcWriteAvailable=false).</summary>
    void ResetToDefault();

    /// <summary>
    /// Apply OC, then monitor GPU temperature for <paramref name="testDuration"/>.
    /// If temperature exceeds <paramref name="watchdogTempC"/>, auto-resets and
    /// returns an "aborted" message.
    /// </summary>
    Task<string> ApplyWithWatchdogAsync(
        GpuControlState desired,
        int watchdogTempC,
        TimeSpan testDuration,
        CancellationToken ct);
}
