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
        ["CommandCenter"] = typeof(CommandCenterPage),
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
        ["Learning"] = typeof(LearningPage),
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

        // Immersive chrome: extend content into the title bar so the top is one glass surface.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        StyleCaptionButtons();

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
        // Set the restore (un-maximized) size from saved preferences. Maximizing happens
        // after the window is activated (see ApplyStartupWindowState) — doing it here in the
        // constructor, before the window is shown, does not reliably stick.
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

    /// <summary>Maximize on launch if configured. Called after Activate() so it sticks reliably.</summary>
    public void ApplyStartupWindowState()
    {
        if (_settingsService.Settings.StartMaximized
            && !_settingsService.Settings.StartMinimized
            && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private bool _shuttingDown;

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Tray "Exit" sets IsExiting and runs its own teardown — let that close proceed.
        if (IsExiting || _shuttingDown) return;
        _shuttingDown = true;

        // Graceful shutdown with timeout to prevent zombie processes.
        // Services may not respond to StopAsync, so we use a 5-second hard deadline.
        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    _settingsService.Save();
                    App.GetService<ITrayIconService>().Hide();
                    (App.GetService<ISensorService>() as IDisposable)?.Dispose();

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await App.GetHost().StopAsync(cts.Token);
                }
                catch { }
                finally
                {
                    Environment.Exit(0);
                }
            });
        }
        catch
        {
            Environment.Exit(0);
        }
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

        // The rail now holds only Command Center + the 5 hubs + Settings. Any older page-level
        // last-nav value falls back to the home. Setting SelectedItem fires SelectionChanged,
        // which performs the navigation.
        var lastNav = _settingsService.Settings.LastNavigationItem;
        if (lastNav != "CommandCenter" && lastNav != "Settings" && HubRegistry.ByTag(lastNav) is null)
            lastNav = "CommandCenter";

        var allItems = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>());
        NavView.SelectedItem =
            allItems.FirstOrDefault(i => i.Tag?.ToString() == lastNav) ??
            allItems.FirstOrDefault(i => i.Tag?.ToString() == "CommandCenter");
    }

    /// <summary>Make the system caption buttons blend into the glass title bar (transparent bg, cyan hover).</summary>
    private void StyleCaptionButtons()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF);
        tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x33, 0x38, 0xBD, 0xF8);
        tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
        tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x55, 0x38, 0xBD, 0xF8);
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
            if (HubRegistry.ByTag(tag) is { } hub)
                _navigationService.NavigateTo(typeof(HubPage), hub);
            else if (PageMap.TryGetValue(tag, out var pageType))
                _navigationService.NavigateTo(pageType);
            else
                return;

            _settingsService.Settings.LastNavigationItem = tag;
            _settingsService.Save();
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
