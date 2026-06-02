using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class SecurityPage : Page
{
    public SecurityViewModel ViewModel { get; }

    public SecurityPage()
    {
        ViewModel = App.GetService<SecurityViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.LoadCommand.ExecuteAsync(null),
            XamlRoot, "Security status load");

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.LoadCommand.ExecuteAsync(null),
            XamlRoot, "Security refresh");

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title             = "Run Quick Scan?",
                Content           = "This will start a Windows Defender Quick Scan in the background. It may take a few minutes.",
                PrimaryButtonText = "Start Scan",
                CloseButtonText   = "Cancel",
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.RunQuickScanCommand.ExecuteAsync(null);
        }, XamlRoot, "Quick scan");
}
