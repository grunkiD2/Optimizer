namespace Optimizer.WinUI.Services;

/// <summary>
/// A PnP device as reported by Win32_PnPEntity.
/// </summary>
public class PnpDevice
{
    public string InstanceId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>PNP device class, e.g. "USB", "Net", "Display", "Processor".</summary>
    public string Class { get; set; } = "";
    /// <summary>Human-readable status string: "OK", "Error", "Disabled", "Unknown".</summary>
    public string Status { get; set; } = "";
    public bool IsEnabled { get; set; }
    /// <summary>
    /// True for devices that must never be disabled (CPU, system disk, primary GPU, system bus, etc.).
    /// Optimizer refuses to call SetEnabled on critical devices.
    /// </summary>
    public bool IsCritical { get; set; }
}

public interface IDeviceControlService
{
    /// <summary>
    /// Lists PnP devices. Optional classFilter limits results to a specific PNP class
    /// (e.g. "USB", "Net"). Pass null for all devices.
    /// </summary>
    Task<IReadOnlyList<PnpDevice>> ListDevicesAsync(string? classFilter = null);

    /// <summary>
    /// Enables or disables the device with the given InstanceId via pnputil.
    /// Returns false if the device is critical, the call fails, or pnputil is unavailable.
    /// </summary>
    Task<bool> SetEnabledAsync(string instanceId, bool enabled);
}
