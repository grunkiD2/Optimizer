using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Commands;
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

    // ── Inert-result couplings (Batch 3): C11 findings, disk-health, bottlenecks ──

    // ItemsRepeater does not set DataContext on realized children (unlike ListView), so we
    // carry the item on Tag="{x:Bind}" and read sender.Tag — the codebase's ItemsRepeater idiom.
    private void FindingGoTo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DiagnosticFinding f) return;
        App.GetService<IPageNavigator>().NavigateTo(CommandCenterPage.CategoryTag(f.Category));
    }

    private async void FindingQuickFix_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DiagnosticFinding { QuickFix: { } fix } f) return;
        var ok = await fix();
        await DialogHelper.InfoAsync(XamlRoot, f.Title,
            ok ? "Rettelsen blev anvendt." : "Rettelsen kunne ikke gennemføres automatisk.");
    }

    // The disk-health forecast carries no drive letter (model/serial only), so we route to
    // Storage where the CHKDSK launcher + drive picker live rather than guess a drive.
    private void DiskScheduleChkdsk_Click(object sender, RoutedEventArgs e)
        => App.GetService<IPageNavigator>().NavigateTo("Storage");

    private async void BottleneckEnd_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProcessBottleneck b) return;
        var confirm = await DialogHelper.ConfirmAsync(XamlRoot, "Afslut proces?",
            $"Afslut {b.ProcessName} (PID {b.Pid})? Ikke-gemt arbejde i processen går tabt.", "Afslut");
        if (!confirm) return;
        var (_, msg) = RowActions.TryEndProcess(b.Pid);
        await DialogHelper.InfoAsync(XamlRoot, "Afslut proces", msg);
    }

    private void BottleneckView_Click(object sender, RoutedEventArgs e)
        => App.GetService<IPageNavigator>().NavigateTo("Performance");
}
