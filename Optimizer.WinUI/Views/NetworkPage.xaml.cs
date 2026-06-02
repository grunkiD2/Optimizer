using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkCategoryViewModel ViewModel { get; }
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public NetworkPage()
    {
        ViewModel = App.GetService<NetworkCategoryViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadDnsAsync();
        ViewModel.StartLatencyMonitor(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopLatencyMonitor();
    }

    private async void RunSpeedTest_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RunSpeedTestCommand.ExecuteAsync(null);

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var preset = ViewModel.DnsPresets.FirstOrDefault(p => p.Id == id);
            if (preset != null)
                await ViewModel.ApplyDnsPresetCommand.ExecuteAsync(preset);
        }
    }

    private async void ResetDns_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ResetDnsCommand.ExecuteAsync(null);

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
        => await ViewModel.FlushDnsCacheCommand.ExecuteAsync(null);

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
        => CategoryPageHelper.OnCardLoaded(sender, XamlRoot, ViewModel, _toggleHandlers);
}
