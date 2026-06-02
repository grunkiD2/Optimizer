using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class CompliancePage : Page
{
    public ComplianceViewModel ViewModel { get; }

    public CompliancePage()
    {
        ViewModel = App.GetService<ComplianceViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) { /* no auto-run; user clicks Run Checks */ }

    private async void RunChecks_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RunChecksCommand.ExecuteAsync(null);

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ExportReportCommand.ExecuteAsync(null);
}
