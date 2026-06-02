using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

using Microsoft.Win32;

using Optimizer.Helpers;
using Optimizer.Services;

using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace Optimizer.ViewModels
{
    public class SettingsViewModel : Observable
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Optimizer";

        private readonly ISettingsService _settingsService;
        private readonly IWindowsOptimizerService _optimizerService;
        private readonly IUpdateService _updateService;
        private string _statusMessage = "Settings are saved automatically.";
        private string _updateStatus = "Configure a feed URL, then check.";

        public SettingsViewModel(ISettingsService settingsService, IWindowsOptimizerService optimizerService, IUpdateService updateService)
        {
            _settingsService = settingsService;
            _optimizerService = optimizerService;
            _updateService = updateService;
            OpenLogsCommand = new RelayCommand(OpenLogs);
            CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            _ = LoadProfilesAsync();
        }

        public string UpdateFeedUrl
        {
            get => S.UpdateFeedUrl;
            set { S.UpdateFeedUrl = value ?? string.Empty; _settingsService.Save(); OnPropertyChanged(nameof(UpdateFeedUrl)); }
        }

        public string UpdateStatus
        {
            get => _updateStatus;
            set => Set(ref _updateStatus, value);
        }

        public ICommand CheckForUpdatesCommand { get; }

        private async Task CheckForUpdatesAsync()
        {
            UpdateStatus = "Checking…";
            var result = await _updateService.CheckAsync();
            UpdateStatus = result.Message;
            if (result.UpdateAvailable && !string.IsNullOrEmpty(result.DownloadUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(result.DownloadUrl) { UseShellExecute = true });
                }
                catch { /* ignore */ }
            }
        }

        /// <summary>Profiles available for the AC/battery auto-switch pickers.</summary>
        public ObservableCollection<SettingsProfile> Profiles { get; } = new();

        private async Task LoadProfilesAsync()
        {
            try
            {
                var profiles = await _optimizerService.ListProfilesAsync();
                Profiles.Clear();
                foreach (var p in profiles) Profiles.Add(p);
                OnPropertyChanged(nameof(OnAcProfileId));
                OnPropertyChanged(nameof(OnBatteryProfileId));
            }
            catch { /* ignore */ }
        }

        public bool AutoSwitchEnabled
        {
            get => S.AutoSwitchEnabled;
            set { S.AutoSwitchEnabled = value; _settingsService.Save(); OnPropertyChanged(nameof(AutoSwitchEnabled)); Saved(); }
        }

        public string OnAcProfileId
        {
            get => S.OnAcProfileId;
            set { S.OnAcProfileId = value ?? string.Empty; _settingsService.Save(); OnPropertyChanged(nameof(OnAcProfileId)); Saved(); }
        }

        public string OnBatteryProfileId
        {
            get => S.OnBatteryProfileId;
            set { S.OnBatteryProfileId = value ?? string.Empty; _settingsService.Save(); OnPropertyChanged(nameof(OnBatteryProfileId)); Saved(); }
        }

        private AppSettings S => _settingsService.Settings;

        public ObservableCollection<string> Themes { get; } = new()
        {
            "FluentLight", "FluentDark", "Windows11Light", "Windows11Dark", "MaterialLight", "MaterialDark"
        };

        public int MetricsRefreshSeconds
        {
            get => S.MetricsRefreshSeconds;
            set { S.MetricsRefreshSeconds = Math.Max(1, value); _settingsService.Save(); OnPropertyChanged(nameof(MetricsRefreshSeconds)); Saved(); }
        }

        public string Theme
        {
            get => S.Theme;
            set { S.Theme = value; _settingsService.Save(); OnPropertyChanged(nameof(Theme)); Saved("Theme saved — applies on restart."); }
        }

        public bool ConfirmBeforeApply
        {
            get => S.ConfirmBeforeApply;
            set { S.ConfirmBeforeApply = value; _settingsService.Save(); OnPropertyChanged(nameof(ConfirmBeforeApply)); Saved(); }
        }

        public bool AlwaysRunAsAdmin
        {
            get => S.AlwaysRunAsAdmin;
            set { S.AlwaysRunAsAdmin = value; _settingsService.Save(); OnPropertyChanged(nameof(AlwaysRunAsAdmin)); Saved("Saved — applies on next launch."); }
        }

        public bool MinimizeToTray
        {
            get => S.MinimizeToTray;
            set { S.MinimizeToTray = value; _settingsService.Save(); OnPropertyChanged(nameof(MinimizeToTray)); Saved(); }
        }

        public bool StartWithWindows
        {
            get => S.StartWithWindows;
            set
            {
                S.StartWithWindows = value;
                _settingsService.Save();
                ApplyStartWithWindows(value);
                OnPropertyChanged(nameof(StartWithWindows));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        public ICommand OpenLogsCommand { get; }

        private void ApplyStartWithWindows(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                {
                    var exe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        key.SetValue(RunValueName, $"\"{exe}\"");
                        StatusMessage = "Optimizer will start with Windows.";
                    }
                }
                else
                {
                    key.DeleteValue(RunValueName, throwOnMissingValue: false);
                    StatusMessage = "Optimizer will no longer start with Windows.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not update start-with-Windows: {ex.Message}";
            }
        }

        private void OpenLogs()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLogging.LogDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open logs folder: {ex.Message}";
            }
        }

        private void Saved(string? message = null) => StatusMessage = message ?? "Saved.";
    }
}
