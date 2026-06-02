using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;

using WindowsOptimizer.Services;

using WinForms = System.Windows.Forms;

namespace Optimizer.Services
{
    public interface ITrayIconService
    {
        void Initialize(Window mainWindow);
    }

    /// <summary>Shows transient notifications (backed by the tray icon's balloon tips).</summary>
    public interface INotificationService
    {
        void Notify(string title, string message, bool isError = false);
    }

    /// <summary>
    /// System tray (notification area) icon: live CPU/mem tooltip, quick profile switching,
    /// and minimize-to-tray. Uses the WinForms NotifyIcon via WPF/WinForms interop.
    /// </summary>
    public class TrayIconService : ITrayIconService, INotificationService, IDisposable
    {
        private readonly IWindowsOptimizerService _optimizer;
        private readonly SystemMonitorService _monitor;
        private readonly ISettingsService _settings;

        private WinForms.NotifyIcon? _notifyIcon;
        private Window? _window;
        private DispatcherTimer? _tooltipTimer;

        public TrayIconService(IWindowsOptimizerService optimizer, SystemMonitorService monitor, ISettingsService settings)
        {
            _optimizer = optimizer;
            _monitor = monitor;
            _settings = settings;
        }

        public void Initialize(Window mainWindow)
        {
            _window = mainWindow;

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Visible = true,
                Text = "Optimizer"
            };
            _notifyIcon.DoubleClick += (_, _) => ShowWindow();

            var menu = new WinForms.ContextMenuStrip();
            menu.Opening += (_, _) => BuildMenu(menu);
            _notifyIcon.ContextMenuStrip = menu;

            _window.StateChanged += OnWindowStateChanged;

            _tooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _tooltipTimer.Tick += (_, _) => UpdateTooltip();
            _tooltipTimer.Start();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    var icon = Icon.ExtractAssociatedIcon(exe);
                    if (icon != null) return icon;
                }
            }
            catch { /* fall through */ }
            return SystemIcons.Application;
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window?.WindowState == WindowState.Minimized && _settings.Settings.MinimizeToTray)
            {
                _window.Hide();
                _notifyIcon?.ShowBalloonTip(2000, "Optimizer", "Still running in the tray.", WinForms.ToolTipIcon.Info);
            }
        }

        private void ShowWindow()
        {
            if (_window == null) return;
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private void BuildMenu(WinForms.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            menu.Items.Add("Open Optimizer", null, (_, _) => ShowWindow());

            var profilesItem = new WinForms.ToolStripMenuItem("Apply profile");
            try
            {
                var profiles = _optimizer.ListProfilesAsync().GetAwaiter().GetResult();
                foreach (var profile in profiles)
                {
                    var id = profile.Id;
                    var name = profile.Name;
                    profilesItem.DropDownItems.Add(name, null, async (_, _) => await ApplyProfile(id, name));
                }
            }
            catch { /* ignore */ }
            profilesItem.Enabled = profilesItem.DropDownItems.Count > 0;
            menu.Items.Add(profilesItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        }

        private async Task ApplyProfile(string id, string name)
        {
            try
            {
                var ok = await _optimizer.ApplyProfileAsync(id);
                _notifyIcon?.ShowBalloonTip(2500, "Optimizer",
                    ok ? $"Applied profile '{name}'." : $"Failed to apply '{name}'.",
                    ok ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                _notifyIcon?.ShowBalloonTip(2500, "Optimizer", $"Error applying '{name}': {ex.Message}", WinForms.ToolTipIcon.Error);
            }
        }

        private void UpdateTooltip()
        {
            Task.Run(() =>
            {
                try
                {
                    var s = _monitor.CollectSnapshot();
                    var memPercent = s.TotalPhysicalMemory > 0
                        ? (s.TotalPhysicalMemory - s.AvailablePhysicalMemory) * 100.0 / s.TotalPhysicalMemory
                        : 0;
                    var text = $"Optimizer · CPU {s.CpuUsagePercentage:F0}% · Mem {memPercent:F0}%";
                    _window?.Dispatcher.Invoke(() =>
                    {
                        if (_notifyIcon != null)
                        {
                            // NotifyIcon.Text is capped at 63 characters.
                            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
                        }
                    });
                }
                catch { /* ignore tooltip update errors */ }
            });
        }

        public void Notify(string title, string message, bool isError = false)
        {
            try
            {
                _notifyIcon?.ShowBalloonTip(3000, title, message,
                    isError ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
            }
            catch { /* notifications are best-effort */ }
        }

        public void Dispose()
        {
            _tooltipTimer?.Stop();
            if (_window != null) _window.StateChanged -= OnWindowStateChanged;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
