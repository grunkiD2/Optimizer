using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class ReportsPage : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        ViewModel = App.GetService<ReportsViewModel>();
        InitializeComponent();
    }

    // Card click selects that report type and immediately generates
    private void ReportTypeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ReportType type)
        {
            ViewModel.SelectedReportType = type;
            ViewModel.GenerateCommand.Execute(null);
        }
    }

    // Open the folder containing the saved report (or the default reports folder)
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = string.IsNullOrEmpty(ViewModel.LastSavedPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Optimizer Reports")
                : Path.GetDirectoryName(ViewModel.LastSavedPath) ?? "";

            if (!string.IsNullOrEmpty(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to open reports folder", ex);
        }
    }
}
