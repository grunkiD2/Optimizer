using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Optimizer.WinUI.Helpers;
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

    // ── Row context menu (Batch 3) ───────────────────────────────────────────
    private static EventLogEntryInfo? EntryOf(object sender)
        => (sender as FrameworkElement)?.DataContext as EventLogEntryInfo;

    private void EventCopy_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } en) return;
        RowActions.CopyText(
            $"{en.TimeText}  [{en.Level}]  {en.Source}  (Event {en.EventId}, {en.LogName})\n{en.Message}");
    }

    private void EventOpenViewer_Click(object sender, RoutedEventArgs e)
        => RowActions.ShellOpen("eventvwr.msc");

    private void EventSearch_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } en) return;
        RowActions.SearchOnline($"Windows Event ID {en.EventId} {en.Source}");
    }
}
