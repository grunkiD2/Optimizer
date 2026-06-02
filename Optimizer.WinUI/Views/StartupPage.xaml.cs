using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class StartupPage : Page
{
    public StartupCategoryViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = App.GetService<StartupCategoryViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadBootMetricsAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadBootMetricsAsync();
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.Tag is not string key) return;

        var entry = ViewModel.Entries.FirstOrDefault(en => en.Key == key);
        if (entry == null) return;

        // Only act if the toggle state differs from the current model state
        // (prevents re-entrancy when Load() refreshes the list)
        if (toggle.IsOn != entry.Enabled)
            ViewModel.ToggleEntryCommand.Execute(entry);
    }
}
