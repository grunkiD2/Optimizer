using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class DevicesPage : Page
{
    public DevicesViewModel ViewModel { get; }

    public DevicesPage()
    {
        ViewModel = App.GetService<DevicesViewModel>();
        InitializeComponent();
        // Default filter selection
        ViewModel.ClassFilter = "All";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void DeviceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        if (toggle.Tag is not string instanceId) return;

        // Find the device in the current list
        var device = ViewModel.Devices.FirstOrDefault(d =>
            string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

        if (device == null) return;

        var intendedState = toggle.IsOn; // true = enable, false = disable

        // Guard: never disable critical devices (belt-and-suspenders on top of service)
        if (device.IsCritical && !intendedState)
        {
            // Revert the toggle visually — the service would refuse anyway
            toggle.IsOn = true;
            return;
        }

        // Require confirmation before disabling
        if (!intendedState)
        {
            var dialog = new ContentDialog
            {
                Title           = "Disable device?",
                Content         = $"Disabling '{device.Name}' may affect system functionality. Continue?",
                PrimaryButtonText  = "Disable",
                CloseButtonText    = "Cancel",
                DefaultButton   = ContentDialogButton.Close,
                XamlRoot        = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                // User cancelled — revert toggle
                toggle.IsOn = device.IsEnabled;
                return;
            }
        }

        var ok = await ViewModel.ToggleDeviceAsync(device, intendedState);
        if (!ok)
        {
            // Revert toggle on failure
            toggle.IsOn = device.IsEnabled;
        }
    }
}
