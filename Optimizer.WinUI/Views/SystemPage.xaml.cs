using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class SystemPage : Page
{
    public SystemCategoryViewModel ViewModel { get; }
    private readonly SettingsService _settings;
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public SystemPage()
    {
        ViewModel = App.GetService<SystemCategoryViewModel>();
        _settings = App.GetService<SettingsService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadPrivacyAsync();
    }

    private async void PrivacyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is string id)
        {
            var setting = ViewModel.PrivacySettings.FirstOrDefault(s => s.Id == id);
            if (setting != null && setting.IsPrivacyFriendly != toggle.IsOn)
                await ViewModel.ToggleAsync(setting, toggle.IsOn);
        }
    }

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
