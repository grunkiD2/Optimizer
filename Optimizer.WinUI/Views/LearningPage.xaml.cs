using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;

namespace Optimizer.WinUI.Views;

public sealed partial class LearningPage : Page
{
    public LearningDashboardViewModel ViewModel { get; }

    public LearningPage()
    {
        ViewModel = App.GetService<LearningDashboardViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void AcceptSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuggestionRow row })
            await ViewModel.AcceptSuggestionCommand.ExecuteAsync(row);
    }

    private async void RejectSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuggestionRow row })
            await ViewModel.RejectSuggestionCommand.ExecuteAsync(row);
    }

    private async void AcknowledgeAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AlertRow row })
            await ViewModel.AcknowledgeAlertCommand.ExecuteAsync(row);
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.SuggestedFileName = $"optimizer-learning-{DateTime.Now:yyyyMMdd-HHmmss}";

            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            await File.WriteAllTextAsync(file.Path, ViewModel.BuildReport());
        }
        catch
        {
            // Export is best-effort; the dashboard already shows the data on screen.
        }
    }
}
