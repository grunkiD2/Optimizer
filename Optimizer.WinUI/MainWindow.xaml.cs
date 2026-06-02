using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly SettingsService _settingsService;

    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Performance"] = typeof(PerformancePage),
        ["Network"] = typeof(NetworkPage),
        ["Storage"] = typeof(StoragePage),
        ["System"] = typeof(SystemPage),
        ["Startup"] = typeof(StartupPage),
        ["Hardware"] = typeof(HardwarePage),
        ["Tuning"]   = typeof(TuningPage),
        ["Diagnostics"] = typeof(DiagnosticsPage),
        ["Recommendations"] = typeof(RecommendationsPage),
        ["Updates"] = typeof(UpdatesPage),
        ["Security"] = typeof(SecurityPage),
        ["Services"] = typeof(ServicesPage),
        ["Profiles"] = typeof(ProfilesPage),
        ["History"] = typeof(HistoryPage),
        ["EventLogs"] = typeof(EventLogsPage),
        ["Reports"]   = typeof(ReportsPage),
        ["Settings"]  = typeof(SettingsPage),
    };

    public MainWindow(NavigationService navigationService, SettingsService settingsService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _settingsService = settingsService;
        _navigationService.Frame = ContentFrame;

        Title = "Optimizer";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)_settingsService.Settings.WindowWidth,
            (int)_settingsService.Settings.WindowHeight));

        // Hook close button — minimize to tray when setting is enabled
        AppWindow.Closing += AppWindow_Closing;

        InitializeElevationState();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_settingsService.Settings.MinimizeToTray)
        {
            args.Cancel = true;
            AppWindow.Hide();
        }
    }

    private void InitializeElevationState()
    {
        var elevationService = App.GetService<IElevationService>();
        if (elevationService.IsElevated)
        {
            ElevatedInfoBar.Visibility = Visibility.Visible;
            ElevationInfoBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            ElevationInfoBar.Visibility = Visibility.Visible;
            ElevatedInfoBar.Visibility = Visibility.Collapsed;
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // First-launch: show onboarding wizard before the normal navigation
        if (!_settingsService.Settings.HasCompletedOnboarding)
        {
            NavView.SelectedItem = null;
            ContentFrame.Navigate(typeof(OnboardingPage));
            return;
        }

        var lastNav = _settingsService.Settings.LastNavigationItem;
        if (!PageMap.ContainsKey(lastNav)) lastNav = "Dashboard";

        var allItems = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>());
        foreach (var item in allItems)
        {
            if (item.Tag?.ToString() == lastNav)
            {
                NavView.SelectedItem = item;
                break;
            }
        }

        _navigationService.NavigateTo(PageMap[lastNav]);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _navigationService.NavigateTo(typeof(SettingsPage));
            _settingsService.Settings.LastNavigationItem = "Settings";
            _settingsService.Save();
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (PageMap.TryGetValue(tag, out var pageType))
            {
                _navigationService.NavigateTo(pageType);
                _settingsService.Settings.LastNavigationItem = tag;
                _settingsService.Save();
            }
        }
    }

    private async void RelaunchElevated_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Relaunch as Administrator?",
            Content = "The app will close and reopen with elevated permissions. Your current state will be preserved.",
            PrimaryButtonText = "🛡️ Relaunch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _settingsService.Save();
            var elevationService = App.GetService<IElevationService>();
            if (elevationService.TryRelaunchElevated())
                Close();
        }
    }
}
