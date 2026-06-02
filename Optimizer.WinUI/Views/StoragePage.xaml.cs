using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class StoragePage : Page
{
    public StorageCategoryViewModel ViewModel { get; }
    private readonly SettingsService _settings;
    private readonly ISystemRepairService _repair;
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public StoragePage()
    {
        ViewModel = App.GetService<StorageCategoryViewModel>();
        _settings = App.GetService<SettingsService>();
        _repair   = App.GetService<ISystemRepairService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadDiskHealthAsync();
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

    private void LargeFileOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string fullPath)
        {
            var file = ViewModel.LargeFiles.FirstOrDefault(f => f.FullPath == fullPath);
            if (file is not null)
                StorageCategoryViewModel.OpenInExplorer(file);
        }
    }

    // ── CHKDSK launcher ──────────────────────────────────────────────────────

    private async void RunChkdsk_Click(object sender, RoutedEventArgs e)
    {
        var drive = "C:";
        if (DriveSelector.SelectedItem is ComboBoxItem item && item.Content is string sel)
            drive = sel;

        var ok = await _repair.LaunchChkdskAsync(drive);

        var dlg = new ContentDialog
        {
            Title = ok ? "CHKDSK Scheduled" : "CHKDSK Failed",
            Content = ok
                ? $"CHKDSK has been scheduled for {drive} on the next system reboot."
                : $"Failed to schedule CHKDSK for {drive}. Ensure the app is running as Administrator.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dlg.ShowAsync();
    }
}
