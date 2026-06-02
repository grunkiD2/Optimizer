using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly IThemeService _themeService;

    // Flag to suppress partial-method saves while bulk-loading
    private bool _isLoading;

    [ObservableProperty] private string selectedTheme = "Dark";
    [ObservableProperty] private string selectedBackdrop = "Mica";
    [ObservableProperty] private string accentColorHex = "#3B82F6";
    [ObservableProperty] private int metricsRefreshSeconds = 1;
    [ObservableProperty] private int chartHistorySeconds = 60;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool confirmBeforeApply;
    [ObservableProperty] private bool minimizeToTray;
    [ObservableProperty] private bool startMinimized;

    // Notification toggles
    [ObservableProperty] private bool notifyPerformance;
    [ObservableProperty] private bool notifyStorage;
    [ObservableProperty] private bool notifyHardware;
    [ObservableProperty] private bool notifySecurity;
    [ObservableProperty] private bool notifyRecommendations;
    [ObservableProperty] private bool notifyOptimizations;

    public string CategoryName => "Settings";
    public string CategoryIcon => ""; // Settings gear icon

    // Collections for dropdowns
    public List<string> ThemeOptions { get; } = ["Light", "Dark", "Default"];
    public List<string> BackdropOptions { get; } = ["None", "Acrylic", "MicaAlt", "Mica"];

    public SettingsViewModel(SettingsService settingsService, HistoryService historyService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        _themeService = themeService;
    }

    public void Load()
    {
        _isLoading = true;
        try
        {
            var s = _settingsService.Settings;
            SelectedTheme = s.Theme ?? "Dark";
            SelectedBackdrop = s.BackdropMaterial ?? "Mica";
            AccentColorHex = s.AccentColor ?? "#3B82F6";
            MetricsRefreshSeconds = s.MetricsRefreshSeconds;
            ChartHistorySeconds = s.ChartHistorySeconds;
            ConfirmBeforeApply = s.ConfirmBeforeApply;
            MinimizeToTray = s.MinimizeToTray;
            StartMinimized = s.StartMinimized;

            // Notification toggles
            NotifyPerformance     = s.NotifyPerformance;
            NotifyStorage         = s.NotifyStorage;
            NotifyHardware        = s.NotifyHardware;
            NotifySecurity        = s.NotifySecurity;
            NotifyRecommendations = s.NotifyRecommendations;
            NotifyOptimizations   = s.NotifyOptimizations;

            // Reflect actual registry state rather than just saved preference
            StartWithWindows = IsAppRegisteredInStartup();
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.Theme = value;
        _settingsService.Save();
        _themeService.ApplyTheme(value);
    }

    partial void OnSelectedBackdropChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.BackdropMaterial = value;
        _settingsService.Save();
        _themeService.ApplyBackdrop(value);
    }

    partial void OnAccentColorHexChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AccentColor = value;
        _settingsService.Save();
    }

    partial void OnMetricsRefreshSecondsChanged(int value)
    {
        if (_isLoading) return;
        _settingsService.Settings.MetricsRefreshSeconds = value;
        _settingsService.Save();
    }

    partial void OnChartHistorySecondsChanged(int value)
    {
        if (_isLoading) return;
        _settingsService.Settings.ChartHistorySeconds = value;
        _settingsService.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.StartWithWindows = value;
        _settingsService.Save();
        SetAppStartupRegistry(value);
    }

    partial void OnConfirmBeforeApplyChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.ConfirmBeforeApply = value;
        _settingsService.Save();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.MinimizeToTray = value;
        _settingsService.Save();
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.StartMinimized = value;
        _settingsService.Save();
    }

    partial void OnNotifyPerformanceChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifyPerformance = value;
        _settingsService.Save();
    }

    partial void OnNotifyStorageChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifyStorage = value;
        _settingsService.Save();
    }

    partial void OnNotifyHardwareChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifyHardware = value;
        _settingsService.Save();
    }

    partial void OnNotifySecurityChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifySecurity = value;
        _settingsService.Save();
    }

    partial void OnNotifyRecommendationsChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifyRecommendations = value;
        _settingsService.Save();
    }

    partial void OnNotifyOptimizationsChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.NotifyOptimizations = value;
        _settingsService.Save();
    }

    [RelayCommand]
    public void ResetSettings()
    {
        _settingsService.Reset();
        Load();

        // Re-apply theme and backdrop from the newly reset defaults
        _themeService.ApplyTheme(_settingsService.Settings.Theme ?? "Dark");
        _themeService.ApplyBackdrop(_settingsService.Settings.BackdropMaterial ?? "Mica");
    }

    [RelayCommand]
    public void ClearHistory()
    {
        _historyService.Clear();
    }

    // ── Start-with-Windows helpers ────────────────────────────────────────────

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunValueName = "Optimizer";

    private static bool IsAppRegisteredInStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppRunValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetAppStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(AppRunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppRunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Non-fatal — setting may not be writable in all environments
        }
    }
}
