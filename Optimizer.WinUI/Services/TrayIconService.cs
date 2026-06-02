using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace Optimizer.WinUI.Services;

public interface ITrayIconService
{
    void Initialize(Window window);
    void Show();
    void Hide();
    void ShowBalloon(string title, string message);
}

public class TrayIconService : ITrayIconService
{
    private TaskbarIcon? _trayIcon;
    private Window? _window;
    private readonly SettingsService _settingsService;
    private readonly ProfileService _profileService;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public TrayIconService(SettingsService settingsService, ProfileService profileService)
    {
        _settingsService = settingsService;
        _profileService = profileService;
    }

    public void Initialize(Window window)
    {
        _window = window;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Optimizer",
            // Use a generated icon with a glyph so no image file is required
            IconSource = new H.NotifyIcon.GeneratedIconSource
            {
                Text = "", // SystemInformation glyph
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 59, 130, 246)), // #3B82F6 accent
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
            },
            // Context menu shows on right-click (default) — left-click restores
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick,
        };

        // Build context menu flyout
        var menu = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = "Open Dashboard" };
        openItem.Click += (_, _) => RestoreWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var cleanupItem = new MenuFlyoutItem { Text = "Run Quick Cleanup" };
        cleanupItem.Click += (_, _) => RunQuickCleanupAsync();
        menu.Items.Add(cleanupItem);

        // Profile submenu — populated from built-in presets
        var profilesMenu = new MenuFlyoutSubItem { Text = "Switch Profile" };
        PopulateProfilesMenu(profilesMenu);
        menu.Items.Add(profilesMenu);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menu;

        // Left-click restores the window
        _trayIcon.LeftClickCommand = new RelayCommand(RestoreWindow);

        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Show()
    {
        if (_trayIcon == null) return;
        // Re-create if previously disposed
        if (!_trayIcon.IsCreated)
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Hide()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon?.ShowNotification(title: title, message: message);
    }

    private void RestoreWindow()
    {
        if (_window == null) return;

        // Use H.NotifyIcon's WindowExtensions.Show which properly handles WinUI3
        _window.Show(disableEfficiencyMode: true);
        _window.Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    private void PopulateProfilesMenu(MenuFlyoutSubItem menu)
    {
        var presets = _profileService.BuiltInPresets;
        if (presets.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "(No profiles)", IsEnabled = false });
            return;
        }

        foreach (var preset in presets)
        {
            var item = new MenuFlyoutItem { Text = preset.Name };
            var profileId = preset.Id; // capture for closure
            item.Click += async (_, _) =>
            {
                try { await _profileService.ApplyPresetAsync(profileId); }
                catch { /* non-fatal */ }
            };
            menu.Items.Add(item);
        }
    }

    private async void RunQuickCleanupAsync()
    {
        // Apply the "Safe Cleanup" built-in preset if available; otherwise no-op
        var safePreset = _profileService.BuiltInPresets
            .FirstOrDefault(p => p.Name.Contains("Safe", StringComparison.OrdinalIgnoreCase)
                               || p.Name.Contains("Clean", StringComparison.OrdinalIgnoreCase));
        if (safePreset != null)
        {
            try { await _profileService.ApplyPresetAsync(safePreset.Id); }
            catch { /* non-fatal */ }
        }
    }

    private void ExitApplication()
    {
        Hide();
        // Environment.Exit is the reliable exit path for unpackaged WinUI 3
        Environment.Exit(0);
    }

    // ── Minimal ICommand adapter ──────────────────────────────────────────────

    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
