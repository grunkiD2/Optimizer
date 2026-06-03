using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly ISettingsService _settingsService;

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
        ["Marketplace"] = typeof(MarketplacePage),
        ["Plugins"]     = typeof(PluginsPage),
        ["History"] = typeof(HistoryPage),
        ["EventLogs"] = typeof(EventLogsPage),
        ["Reports"]      = typeof(ReportsPage),
        ["Settings"]     = typeof(SettingsPage),
        ["DisplayTest"]  = typeof(DisplayTestPage),
        ["Devices"]      = typeof(DevicesPage),
        ["Fleet"]        = typeof(FleetPage),
        ["Templates"]    = typeof(TemplatesPage),
        ["Compliance"]   = typeof(CompliancePage),
    };

    /// <summary>
    /// Set to true by TrayIconService before calling Close() so the minimize-to-tray
    /// handler knows this is a genuine exit and should not intercept the close.
    /// </summary>
    public bool IsExiting { get; set; }

    public MainWindow(NavigationService navigationService, ISettingsService settingsService)
    {
        InitializeComponent();

        // Capture the UI-thread dispatcher so background-thread events (event bus) can marshal to UI.
        App.UiDispatcher = DispatcherQueue;

        _navigationService = navigationService;
        _settingsService = settingsService;
        _navigationService.Frame = ContentFrame;

        // Wire the assistant's navigate_to_page command to real shell navigation.
        var navigator = (PageNavigator)App.GetService<IPageNavigator>();
        navigator.Configure(
            PageMap.Keys.ToList(),
            tag =>
            {
                var match = PageMap.Keys.FirstOrDefault(k => string.Equals(k, tag, StringComparison.OrdinalIgnoreCase));
                if (match is null) return false;
                var pageType = PageMap[match];
                DispatcherQueue.TryEnqueue(() => _navigationService.NavigateTo(pageType));
                return true;
            });

        Title = "Optimizer";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)_settingsService.Settings.WindowWidth,
            (int)_settingsService.Settings.WindowHeight));

        // Ctrl+` toggles the console dock. VK_OEM_3 (192) has no named VirtualKey member,
        // so it can't be declared in XAML — register it here with an explicit cast.
        var consoleAccel = new KeyboardAccelerator
        {
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
            Key = (Windows.System.VirtualKey)192,
        };
        consoleAccel.Invoked += ConsoleAccel_Invoked;
        RootGrid.KeyboardAccelerators.Add(consoleAccel);

        // The X (and taskbar right-click → Close) quit the app for real.
        AppWindow.Closing += AppWindow_Closing;

        InitializeElevationState();

        // Console dock is open by default on the right — no Ctrl+` needed.
        EnsureDockPanel();
        SetConsoleVisible(true);
    }

    private bool _shuttingDown;

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Tray "Exit" sets IsExiting and runs its own teardown — let that close proceed.
        if (IsExiting || _shuttingDown) return;
        _shuttingDown = true;

        // Best-effort cleanup, then HARD-exit so the process can never linger.
        // (Awaiting host StopAsync here can hang on background services and leave a zombie
        //  process — the "can't close the app" symptom. Environment.Exit is guaranteed.)
        try { _settingsService.Save(); } catch { }
        try { App.GetService<ITrayIconService>().Hide(); } catch { }            // remove tray icon
        try { (App.GetService<ISensorService>() as IDisposable)?.Dispose(); } catch { } // release LHM/PawnIO driver
        Environment.Exit(0);
    }

    private void InitializeElevationState()
    {
        var elevationService = App.GetService<IElevationService>();
        if (elevationService.IsElevated)
        {
            // Running elevated is the expected/quiet state — don't show a banner for it.
            ElevatedInfoBar.Visibility = Visibility.Collapsed;
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

    // ── Console dock ────────────────────────────────────────────────────────

    private ConsolePanel? _dockPanel;
    private ConsoleWindow? _popOut;

    private void EnsureDockPanel()
    {
        if (_dockPanel != null) return;
        _dockPanel = new ConsolePanel();
        _dockPanel.CollapseRequested += (_, _) => SetConsoleVisible(false);
        _dockPanel.PopOutRequested += (_, _) => PopOutConsole();
        ConsoleDockHost.Child = _dockPanel;
    }

    public void SetConsoleVisible(bool visible)
    {
        EnsureDockPanel();
        ConsoleDockHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ToggleConsole() =>
        SetConsoleVisible(ConsoleDockHost.Visibility != Visibility.Visible);

    public void FocusAssistant()
    {
        SetConsoleVisible(true);
        _dockPanel?.FocusAssistant();
    }

    private void PopOutConsole()
    {
        SetConsoleVisible(false);
        _popOut = new ConsoleWindow();
        _popOut.ReDockRequested += (_, _) => { _popOut = null; SetConsoleVisible(true); };
        _popOut.Activate();
    }

    private void ConsoleAccel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleConsole();
        args.Handled = true;
    }

    private async void OmniboxAccel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var box = new TextBox { PlaceholderText = "Ask the assistant…", MinWidth = 360 };
        var dialog = new ContentDialog
        {
            Title = "Assistant",
            Content = box,
            PrimaryButtonText = "Ask",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            var text = box.Text;
            FocusAssistant();
            var vm = App.GetService<ViewModels.AssistantViewModel>();
            vm.Input = text;
            if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
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
