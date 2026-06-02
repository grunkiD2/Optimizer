using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SettingsService : ISettingsService
{
    private static readonly string FilePath = AppPaths.GetDataFile("app-settings.json");

    public AppSettings Settings { get; private set; } = new();

    /// <inheritdoc />
    public event Action? SettingsChanged;

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            Settings = new();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
        SettingsChanged?.Invoke();
    }

    public void Reset()
    {
        Settings = new AppSettings();
        Save();
    }

    /// <inheritdoc />
    public void ApplyRemoteSettings(AppSettings remote)
    {
        // --- Sync these user-preference fields from remote ---
        Settings.MetricsRefreshSeconds     = remote.MetricsRefreshSeconds;
        Settings.ChartHistorySeconds       = remote.ChartHistorySeconds;
        Settings.Theme                     = remote.Theme;
        Settings.BackdropMaterial          = remote.BackdropMaterial;
        Settings.AccentColor               = remote.AccentColor;
        Settings.ConfirmBeforeApply        = remote.ConfirmBeforeApply;
        Settings.MinimizeToTray            = remote.MinimizeToTray;
        Settings.StartMinimized            = remote.StartMinimized;
        Settings.StartWithWindows          = remote.StartWithWindows;
        Settings.NotifyPerformance         = remote.NotifyPerformance;
        Settings.NotifyStorage             = remote.NotifyStorage;
        Settings.NotifyHardware            = remote.NotifyHardware;
        Settings.NotifySecurity            = remote.NotifySecurity;
        Settings.NotifyRecommendations     = remote.NotifyRecommendations;
        Settings.NotifyOptimizations       = remote.NotifyOptimizations;
        Settings.UsageProfile              = remote.UsageProfile;
        Settings.Language                  = remote.Language;

        // --- Per-device fields intentionally NOT synced ---
        // LastNavigationItem  — per-device UI state
        // WindowWidth/Height  — per-monitor layout
        // ApiEnabled/Port/Token — per-machine API server config
        // CloudSyncEnabled/CloudServerUrl — per-device sync state
        // HasCompletedOnboarding — per-device onboarding state

        Save();
    }
}
