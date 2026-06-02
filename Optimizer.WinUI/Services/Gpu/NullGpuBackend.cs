using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

/// <summary>
/// Fallback backend that is always unavailable.
/// Used when no real backend (NVAPI/ADL) reports a GPU present.
/// All operations are safe no-ops.
/// </summary>
public sealed class NullGpuBackend : IGpuControlBackend
{
    public GpuVendor Vendor => GpuVendor.Unknown;
    public bool IsAvailable => false;
    public string? UnavailableReason => "No supported GPU control backend available.";

    public GpuControlCapabilities GetCapabilities() => new()
    {
        CanReadTelemetry   = false,
        CanSetCoreOffset   = false,
        CanSetMemoryOffset = false,
        CanSetPowerLimit   = false,
        CanSetTempLimit    = false,
        CanSetFan          = false,
    };

    public bool TryApply(GpuControlState clampedState, out string error)
    {
        error = UnavailableReason ?? "No GPU backend available.";
        return false;
    }

    public void ResetToDefault()
    {
        // No-op: nothing was applied, nothing to reset.
    }
}
