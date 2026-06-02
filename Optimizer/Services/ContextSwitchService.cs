using Microsoft.Win32;

using WindowsOptimizer.Services;

using WinForms = System.Windows.Forms;

namespace Optimizer.Services
{
    public interface IContextSwitchService
    {
        void Start();
        void Stop();
    }

    /// <summary>
    /// Watches the AC/battery power state and auto-applies a mapped profile when it changes.
    /// Disabled unless the user opts in and chooses profiles in Settings.
    /// </summary>
    public class ContextSwitchService : IContextSwitchService, IDisposable
    {
        private readonly IWindowsOptimizerService _optimizer;
        private readonly ISettingsService _settings;
        private readonly INotificationService _notifications;
        private bool? _lastOnline;
        private bool _started;

        public ContextSwitchService(IWindowsOptimizerService optimizer, ISettingsService settings, INotificationService notifications)
        {
            _optimizer = optimizer;
            _settings = settings;
            _notifications = notifications;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _ = ApplyForCurrentStateAsync();
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange)
            {
                _ = ApplyForCurrentStateAsync();
            }
        }

        private async Task ApplyForCurrentStateAsync()
        {
            try
            {
                if (!_settings.Settings.AutoSwitchEnabled) return;

                var online = WinForms.SystemInformation.PowerStatus.PowerLineStatus == WinForms.PowerLineStatus.Online;
                if (_lastOnline == online) return; // no real transition
                _lastOnline = online;

                var profileId = online ? _settings.Settings.OnAcProfileId : _settings.Settings.OnBatteryProfileId;
                if (string.IsNullOrEmpty(profileId)) return;

                var ok = await _optimizer.ApplyProfileAsync(profileId);
                var profile = (await _optimizer.ListProfilesAsync()).FirstOrDefault(p => p.Id == profileId);
                _notifications.Notify("Auto-switch",
                    ok ? $"Applied '{profile?.Name}' ({(online ? "plugged in" : "on battery")})." : "Auto-switch failed.",
                    !ok);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Context auto-switch failed");
            }
        }

        public void Dispose() => Stop();
    }
}
