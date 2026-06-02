using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class ServicesPage : Page
{
    public ServicesViewModel ViewModel { get; }

    // Guard against re-entrant SelectionChanged fired while we reload
    private bool _suppressStartupTypeChange;

    public ServicesPage()
    {
        ViewModel = App.GetService<ServicesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serviceName)
        {
            var svc = ViewModel.Services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (svc != null) await ViewModel.ToggleServiceAsync(svc);
        }
    }

    private async void StartupType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStartupTypeChange) return;

        if (sender is ComboBox cb &&
            cb.Tag is string serviceName &&
            cb.SelectedItem is ComboBoxItem item)
        {
            var svc = ViewModel.Services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (svc != null && svc.StartupType != item.Content?.ToString())
            {
                _suppressStartupTypeChange = true;
                try
                {
                    await ViewModel.SetStartupTypeAsync(svc, item.Content!.ToString()!);
                }
                finally
                {
                    _suppressStartupTypeChange = false;
                }
            }
        }
    }
}
