using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void RunQuickScan_Click(object sender, RoutedEventArgs e)
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
    }
}
