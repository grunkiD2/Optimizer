using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        ViewModel = App.GetService<DiagnosticsViewModel>();
        InitializeComponent();
    }

    private async void QuickScan_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.QuickScanCommand.ExecuteAsync(null),
            XamlRoot, "Quick diagnostics scan");

    private async void FullScan_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.FullScanCommand.ExecuteAsync(null),
            XamlRoot, "Full diagnostics scan");

    private async void ScanDrivers_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.ScanDriversCommand.ExecuteAsync(null),
            XamlRoot, "Driver scan");

    private async void DetectBottlenecks_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.DetectBottlenecksCommand.ExecuteAsync(null),
            XamlRoot, "Bottleneck detection");

    private async void RunNetworkDeep_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.RunNetworkDeepCommand.ExecuteAsync(null),
            XamlRoot, "Network deep scan");

    private async void LoadPredictions_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => ViewModel.LoadPredictionsCommand.ExecuteAsync(null),
            XamlRoot, "Predictions");

    private void OpenDisplayTest_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenDisplayTestCommand.Execute(null);
}
