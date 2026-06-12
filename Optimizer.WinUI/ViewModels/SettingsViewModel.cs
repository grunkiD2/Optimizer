using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.ViewModels;


public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHistoryService _historyService;
    private readonly IThemeService _themeService;
    private readonly IApiHostService _apiHost;
    private readonly IApiKeyStore _apiKeyStore;

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
    [ObservableProperty] private bool startMaximized = true;

    // Console: mirror all engine output to the Activity console (default ON)
    [ObservableProperty] private bool verboseConsole = true;

    // Notification toggles
    [ObservableProperty] private bool notifyPerformance;
    [ObservableProperty] private bool notifyStorage;
    [ObservableProperty] private bool notifyHardware;
    [ObservableProperty] private bool notifySecurity;
    [ObservableProperty] private bool notifyRecommendations;
    [ObservableProperty] private bool notifyOptimizations;

    // Remote API
    [ObservableProperty] private bool apiEnabled;
    [ObservableProperty] private int apiPort = 8765;
    [ObservableProperty] private string apiToken = "";
    [ObservableProperty] private string apiStatus = "Stopped";

    // AI Assistant (Claude API, opt-in, bring-your-own-key)
    [ObservableProperty] private bool assistantEnabled;
    [ObservableProperty] private bool assistantAllowActions = true;
    [ObservableProperty] private string assistantModel = "claude-sonnet-4-6";
    [ObservableProperty] private string apiKeyInput = "";
    [ObservableProperty] private bool hasApiKey;

    public List<string> AssistantModelOptions { get; } =
        ["claude-sonnet-4-6", "claude-haiku-4-5-20251001", "claude-opus-4-8"];

    public string CategoryName => "Settings";
    public string CategoryIcon => ""; // Settings gear icon

    // Collections for dropdowns
    public List<string> ThemeOptions    { get; } = ["Light", "Dark", "Default"];
    public List<string> BackdropOptions { get; } = ["None", "Acrylic", "MicaAlt", "Mica"];

    public SettingsViewModel(
        ISettingsService settingsService,
        IHistoryService historyService,
        IThemeService themeService,
        IApiHostService apiHost,
        IApiKeyStore apiKeyStore)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        _themeService = themeService;
        _apiHost = apiHost;
        _apiKeyStore = apiKeyStore;
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
            StartMaximized = s.StartMaximized;
            VerboseConsole = s.VerboseConsole;

            // Notification toggles
            NotifyPerformance     = s.NotifyPerformance;
            NotifyStorage         = s.NotifyStorage;
            NotifyHardware        = s.NotifyHardware;
            NotifySecurity        = s.NotifySecurity;
            NotifyRecommendations = s.NotifyRecommendations;
            NotifyOptimizations   = s.NotifyOptimizations;

            // Remote API
            ApiEnabled = s.ApiEnabled;
            ApiPort    = s.ApiPort;
            ApiToken   = s.ApiToken;
            ApiStatus  = _apiHost.IsRunning ? $"Running at {_apiHost.ListeningUrl}" : "Stopped";

            // AI Assistant
            AssistantEnabled = s.AssistantEnabled;
            AssistantAllowActions = s.AssistantAllowActions;
            AssistantModel = string.IsNullOrWhiteSpace(s.AssistantModel) ? "claude-sonnet-4-6" : s.AssistantModel;
            HasApiKey = _apiKeyStore.HasKey;

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

    partial void OnVerboseConsoleChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.VerboseConsole = value;
        _settingsService.Save();
    }

    partial void OnStartMaximizedChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.StartMaximized = value;
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

    partial void OnApiEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.ApiEnabled = value;
        _settingsService.Save();
        _ = ToggleApiAsync(value);
    }

    partial void OnApiPortChanged(int value)
    {
        if (_isLoading) return;
        _settingsService.Settings.ApiPort = value;
        _settingsService.Save();
    }

    [RelayCommand]
    public async Task RegenerateTokenAsync()
    {
        var newToken = Guid.NewGuid().ToString();
        ApiToken = newToken;
        _settingsService.Settings.ApiToken = newToken;
        _settingsService.Save();

        // Restart running server with new token
        if (_apiHost.IsRunning)
        {
            await _apiHost.StopAsync();
            await _apiHost.StartAsync(ApiPort, newToken);
            ApiStatus = $"Running at {_apiHost.ListeningUrl}";
        }
    }

    [RelayCommand]
    public void CopyApiToken()
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ApiToken);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private async Task ToggleApiAsync(bool enabled)
    {
        if (enabled)
        {
            await _apiHost.StartAsync(ApiPort, ApiToken);
            ApiStatus = $"Running at {_apiHost.ListeningUrl}";
        }
        else
        {
            await _apiHost.StopAsync();
            ApiStatus = "Stopped";
        }
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

    // ── AI Assistant ──────────────────────────────────────────────────────────

    partial void OnAssistantEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantEnabled = value;
        _settingsService.Save();
    }

    partial void OnAssistantAllowActionsChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantAllowActions = value;
        _settingsService.Save();
    }

    partial void OnAssistantModelChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantModel = value;
        _settingsService.Save();
    }

    [RelayCommand]
    public void SaveApiKey()
    {
        _apiKeyStore.SetKey(ApiKeyInput);
        ApiKeyInput = "";
        HasApiKey = _apiKeyStore.HasKey;
    }

    [RelayCommand]
    public void ClearApiKey()
    {
        _apiKeyStore.Clear();
        HasApiKey = _apiKeyStore.HasKey;
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
