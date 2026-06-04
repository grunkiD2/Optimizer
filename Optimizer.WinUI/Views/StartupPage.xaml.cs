using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

/// <summary>
/// "Startup &amp; Services" — the merged Startup + Services destination from the Optimize hub.
/// Hosts both <see cref="ViewModel"/> (StartupCategoryViewModel, drives the startup-programs
/// panel) and <see cref="ServicesVM"/> (ServicesViewModel, drives the services panel). The
/// in-page <c>Segmented</c> switches between them; both view the same "what runs at boot or
/// in the background" job.
/// </summary>
public sealed partial class StartupPage : Page
{
    public StartupCategoryViewModel ViewModel { get; }
    public ServicesViewModel ServicesVM { get; }

    // Guard against re-entrant SelectionChanged fired while we reload
    private bool _suppressStartupTypeChange;

    /// <summary>0 = Startup Programs, 1 = Services. See HubPage hub-aware navigation.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int idx && SectionSeg is not null
            && idx >= 0 && idx < SectionSeg.Items.Count)
        {
            SectionSeg.SelectedIndex = idx;
        }
    }

    public StartupPage()
    {
        ViewModel  = App.GetService<StartupCategoryViewModel>();
        ServicesVM = App.GetService<ServicesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadBootMetricsAsync();
        await ServicesVM.LoadAsync();
    }

    // ── Section switcher ─────────────────────────────────────────────────────

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PanelStartup is null) return;
        var i = SectionSeg.SelectedIndex;
        PanelStartup.Visibility  = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelServices.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Panel A: Startup programs ────────────────────────────────────────────

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

    // ── Panel B: Services ────────────────────────────────────────────────────

    private async void RefreshServices_Click(object sender, RoutedEventArgs e)
        => await ServicesVM.LoadAsync();

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serviceName)
        {
            var svc = ServicesVM.Services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (svc != null) await ServicesVM.ToggleServiceAsync(svc);
        }
    }

    private async void StartupType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStartupTypeChange) return;

        if (sender is ComboBox cb &&
            cb.Tag is string serviceName &&
            cb.SelectedItem is ComboBoxItem item)
        {
            var svc = ServicesVM.Services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (svc != null && svc.StartupType != item.Content?.ToString())
            {
                _suppressStartupTypeChange = true;
                try
                {
                    await ServicesVM.SetStartupTypeAsync(svc, item.Content!.ToString()!);
                }
                finally
                {
                    _suppressStartupTypeChange = false;
                }
            }
        }
    }
}
