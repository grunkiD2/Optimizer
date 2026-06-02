using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class EventLogsPage : Page
{
    public EventLogsViewModel ViewModel { get; }

    public EventLogsPage()
    {
        ViewModel = App.GetService<EventLogsViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private void Entry_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EventLogEntryInfo entry)
            ViewModel.ShowDetail(entry);
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
        => ViewModel.CloseDetail();

    private async void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
