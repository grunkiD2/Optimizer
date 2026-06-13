using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    private readonly IDeviceControlService _deviceService;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [ObservableProperty] private string? _classFilter;

    public ObservableCollection<PnpDevice> Devices { get; } = [];

    /// <summary>
    /// Available class filter options for the combo-box.
    /// "All" maps to null (no filter).
    /// </summary>
    public IReadOnlyList<string> ClassFilters { get; } =
    [
        "All",
        "USB",
        "Net",
        "Display",
        "Media",
        "AudioEndpoint",
        "Bluetooth",
        "HIDClass",
        "Processor",
        "SCSIAdapter",
        "DiskDrive",
    ];

    public DevicesViewModel(IDeviceControlService deviceService)
    {
        _deviceService = deviceService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = string.Empty;
        try
        {
            var filter = ClassFilter == "All" ? null : ClassFilter;
            var list   = await _deviceService.ListDevicesAsync(filter);

            Devices.Clear();
            foreach (var d in list) Devices.Add(d);

            if (Devices.Count == 0)
                StatusMessage = "No devices found matching the selected filter.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading devices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggle the enabled state of a device.
    /// Returns false if the service refuses (critical device) or the call fails.
    /// </summary>
    public async Task<bool> ToggleDeviceAsync(PnpDevice device, bool enable)
    {
        if (device.IsCritical && !enable)
        {
            StatusMessage = $"'{device.Name}' is a protected system device and cannot be disabled.";
            return false;
        }

        IsLoading = true;
        try
        {
            var ok = await _deviceService.SetEnabledAsync(device.InstanceId, enable);
            StatusMessage = ok
                ? $"'{device.Name}' {(enable ? "enabled" : "disabled")} successfully."
                : $"Failed to {(enable ? "enable" : "disable")} '{device.Name}'.";

            if (ok) await LoadAsync(); // refresh list
            return ok;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Restart a device by disabling then re-enabling it through the service (Batch 3).
    /// Refuses critical devices. Reports progress via StatusMessage; if re-enable fails after
    /// a successful disable, says so plainly so the user knows the device is left disabled.
    /// </summary>
    public async Task<bool> RestartDeviceAsync(PnpDevice device)
    {
        if (device.IsCritical)
        {
            StatusMessage = $"'{device.Name}' is a protected system device and cannot be restarted.";
            return false;
        }

        IsLoading = true;
        try
        {
            var off = await _deviceService.SetEnabledAsync(device.InstanceId, false);
            if (!off)
            {
                StatusMessage = $"Failed to restart '{device.Name}' (could not disable it).";
                return false;
            }

            var on = await _deviceService.SetEnabledAsync(device.InstanceId, true);
            StatusMessage = on
                ? $"'{device.Name}' restarted successfully."
                : $"'{device.Name}' was disabled but FAILED to re-enable — re-enable it in Device Manager.";

            await LoadAsync(); // refresh list to reflect the device's real state
            return on;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnClassFilterChanged(string? value)
    {
        // Reload automatically when filter changes
        _ = LoadAsync();
    }
}
