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
}

public class TrayIconService : ITrayIconService
{
    private TaskbarIcon? _trayIcon;
    private Window? _window;
    private readonly ISettingsService _settingsService;
    private readonly IProfileService _profileService;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public TrayIconService(ISettingsService settingsService, IProfileService profileService)
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

        // Left-click restores the window.
        _trayIcon.LeftClickCommand = new RelayCommand(RestoreWindow);

        // Right-click shows a NATIVE Win32 popup menu at the cursor. The WinUI ContextFlyout
        // is hosted via the main window's XamlRoot and always renders on that window's monitor,
        // so on multi-monitor it lands on the wrong screen. TrackPopupMenuEx always appears at
        // the supplied cursor coordinates, on whichever monitor you right-clicked.
        _trayIcon.RightClickCommand = new RelayCommand(ShowTrayContextMenu);

        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
    }

    // ── Native popup menu (multi-monitor correct) ────────────────────────────

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_POPUP = 0x0010;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint WM_NULL = 0x0000;

    private void ShowTrayContextMenu()
    {
        if (_window is null) return;
        if (!GetCursorPos(out var pt)) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        var submenu = IntPtr.Zero;

        const uint idOpen = 1, idCleanup = 2, idExit = 3, presetBase = 100;
        var presetMap = new Dictionary<uint, string>();

        try
        {
            AppendMenu(menu, MF_STRING, (UIntPtr)idOpen, "Open Dashboard");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)idCleanup, "Run Quick Cleanup");

            var presets = _profileService.BuiltInPresets;
            if (presets.Count > 0)
            {
                submenu = CreatePopupMenu();
                var id = presetBase;
                foreach (var preset in presets)
                {
                    AppendMenu(submenu, MF_STRING, (UIntPtr)id, preset.Name);
                    presetMap[id] = preset.Id;
                    id++;
                }
                AppendMenu(menu, MF_POPUP, (UIntPtr)(ulong)submenu.ToInt64(), "Switch Profile");
            }

            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)idExit, "Exit");

            // The owner window must be foreground for the menu to dismiss on outside-click.
            SetForegroundWindow(hwnd);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); // classic dismiss fix

            switch (cmd)
            {
                case 0: break;                       // cancelled
                case idOpen: RestoreWindow(); break;
                case idCleanup: RunQuickCleanupAsync(); break;
                case idExit: ExitApplication(); break;
                default:
                    if (presetMap.TryGetValue(cmd, out var pid))
                        _ = _profileService.ApplyPresetAsync(pid);
                    break;
            }
        }
        finally
        {
            if (submenu != IntPtr.Zero) DestroyMenu(submenu);
            DestroyMenu(menu);
        }
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
        try
        {
            Hide();  // hide tray icon first

            // Dispose singleton sensor service explicitly to release LHM
            var sensors = App.GetService<ISensorService>();
            if (sensors is IDisposable d) d.Dispose();

            // Close the main window which triggers proper teardown
            var mainWindow = App.GetService<MainWindow>();
            if (mainWindow != null)
            {
                mainWindow.IsExiting = true;
                mainWindow.Close();
            }
        }
        catch { }
        finally
        {
            // Stop hosted services then exit
            Task.Run(async () =>
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await App.GetHost().StopAsync(cts.Token);
                }
                catch { }
                Environment.Exit(0);
            });
        }
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
