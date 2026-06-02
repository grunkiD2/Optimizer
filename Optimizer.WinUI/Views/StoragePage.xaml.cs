using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class StoragePage : Page
{
    public StorageCategoryViewModel ViewModel { get; }
    private readonly ISystemRepairService _repair;
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public StoragePage()
    {
        ViewModel = App.GetService<StorageCategoryViewModel>();
        _repair   = App.GetService<ISystemRepairService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadDiskHealthAsync();
    }

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
        => CategoryPageHelper.OnCardLoaded(sender, XamlRoot, ViewModel, _toggleHandlers);

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
        => await PageExceptionHelper.SafeAsync(async () =>
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
        }, XamlRoot, "CHKDSK");
}
