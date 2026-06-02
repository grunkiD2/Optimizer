using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }

    public RecommendationsPage()
    {
        ViewModel = App.GetService<RecommendationsViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        await ViewModel.LoadInsightsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RefreshCommand.ExecuteAsync(null);

    private async void ResetDismissed_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ResetDismissedCommand.ExecuteAsync(null);

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Recommendation rec)
            await ViewModel.ApplyActionCommand.ExecuteAsync(rec);
    }

    private async void Snooze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
            await ViewModel.SnoozeCommand.ExecuteAsync(id);
    }

    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Recommendation rec)
            await ViewModel.DismissCommand.ExecuteAsync(rec);
    }
}
