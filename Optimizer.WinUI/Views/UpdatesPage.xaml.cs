using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class UpdatesPage : Page
{
    public UpdatesViewModel ViewModel { get; }

    public UpdatesPage()
    {
        ViewModel = App.GetService<UpdatesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e)
        => await ViewModel.CheckForWindowsUpdatesCommand.ExecuteAsync(null);

    private async void UpgradeApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AppUpdateInfo app)
            await ViewModel.UpgradeAppCommand.ExecuteAsync(app);
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title             = "Upgrade All Applications?",
            Content           = "This will silently upgrade all apps with available updates using winget. This may take several minutes.",
            PrimaryButtonText = "Upgrade All",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.UpgradeAllCommand.ExecuteAsync(null);
    }
}
