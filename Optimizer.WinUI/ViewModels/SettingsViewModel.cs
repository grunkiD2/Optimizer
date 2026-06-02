using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;

    // Flag to suppress partial-method saves while bulk-loading
    private bool _isLoading;

    [ObservableProperty] private string selectedTheme = "Dark";
    [ObservableProperty] private string selectedBackdrop = "Mica";
    [ObservableProperty] private string accentColorHex = "#3B82F6";
    [ObservableProperty] private int metricsRefreshSeconds = 1;
    [ObservableProperty] private int chartHistorySeconds = 60;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool confirmBeforeApply;

    public string CategoryName => "Settings";
    public string CategoryIcon => ""; // Settings gear icon

    // Collections for dropdowns
    public List<string> ThemeOptions { get; } = ["Light", "Dark", "Default"];
    public List<string> BackdropOptions { get; } = ["None", "Acrylic", "MicaAlt", "Mica"];

    public SettingsViewModel(SettingsService settingsService, HistoryService historyService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
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
        var window = App.GetService<MainWindow>();
        if (window.Content is FrameworkElement root)
            ThemeHelper.ApplyTheme(root, value);
    }

    partial void OnSelectedBackdropChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.BackdropMaterial = value;
        _settingsService.Save();
        ThemeHelper.ApplyBackdrop(App.GetService<MainWindow>(), value);
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

    [RelayCommand]
    public void ResetSettings()
    {
        _settingsService.Reset();
        Load();

        // Re-apply theme and backdrop from the newly reset defaults
        var window = App.GetService<MainWindow>();
        if (window.Content is FrameworkElement root)
            ThemeHelper.ApplyTheme(root, _settingsService.Settings.Theme);
        ThemeHelper.ApplyBackdrop(window, _settingsService.Settings.BackdropMaterial);
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
