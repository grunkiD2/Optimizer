using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void UndoEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntryViewModel entry })
            await ViewModel.UndoEntryCommand.ExecuteAsync(entry);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title             = "Clear History",
            Content           = "Are you sure? This will delete all optimization history. This cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ClearHistoryCommand.ExecuteAsync(null);
    }
}
