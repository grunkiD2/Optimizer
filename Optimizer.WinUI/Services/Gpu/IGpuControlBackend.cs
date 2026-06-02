using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

public interface IGpuControlBackend
{
    GpuVendor Vendor { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    GpuControlCapabilities GetCapabilities();
    bool TryApply(GpuControlState clampedState, out string error);
    void ResetToDefault();
}
