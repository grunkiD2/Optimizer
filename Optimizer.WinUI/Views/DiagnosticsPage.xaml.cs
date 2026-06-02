using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        => await ViewModel.QuickScanCommand.ExecuteAsync(null);

    private async void FullScan_Click(object sender, RoutedEventArgs e)
        => await ViewModel.FullScanCommand.ExecuteAsync(null);

    private async void ScanDrivers_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ScanDriversCommand.ExecuteAsync(null);

    private async void DetectBottlenecks_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DetectBottlenecksCommand.ExecuteAsync(null);

    private async void RunNetworkDeep_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RunNetworkDeepCommand.ExecuteAsync(null);

    private void OpenDisplayTest_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenDisplayTestCommand.Execute(null);
}
