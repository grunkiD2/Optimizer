using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkCategoryViewModel ViewModel { get; }
    private readonly SettingsService _settings;
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public NetworkPage()
    {
        ViewModel = App.GetService<NetworkCategoryViewModel>();
        _settings = App.GetService<SettingsService>();
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
    {
        if (sender is not OptimizationCard card || card.Tag is not string id) return;

        var model = ViewModel.Optimizations.FirstOrDefault(o => o.Id == id);
        if (model == null) return;

        card.LoadFromInfo(model.Info, model.IsActive, model.IsElevated);

        if (_toggleHandlers.TryGetValue(id, out var oldHandler))
            card.Toggled -= oldHandler;

        EventHandler<bool> handler = async (_, isOn) =>
        {
            if (isOn && _settings.Settings.ConfirmBeforeApply)
            {
                var confirmed = await DialogHelper.ConfirmAsync(
                    XamlRoot,
                    "Confirm Optimization",
                    $"Apply \"{model.Info.Title}\"?");
                if (!confirmed)
                {
                    card.IsActive = false;
                    return;
                }
            }
            await ViewModel.ToggleOptimizationAsync(id, isOn);
        };
        _toggleHandlers[id] = handler;
        card.Toggled += handler;
    }
}
