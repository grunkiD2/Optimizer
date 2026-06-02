using System.Text.Json.Serialization;

namespace Optimizer.WinUI.Models;

public class AppSettings
{
    public int MetricsRefreshSeconds { get; set; } = 1;
    public int ChartHistorySeconds { get; set; } = 60;
    public string Theme { get; set; } = "Dark";
    public string BackdropMaterial { get; set; } = "Mica";
    public string AccentColor { get; set; } = "#3B82F6";
    public bool StartWithWindows { get; set; }
    public bool ConfirmBeforeApply { get; set; } = true;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public string LastNavigationItem { get; set; } = "Dashboard";
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;

    // Notification toggles
    public bool NotifyPerformance     { get; set; } = true;
    public bool NotifyStorage         { get; set; } = true;
    public bool NotifyHardware        { get; set; } = true;
    public bool NotifySecurity        { get; set; } = true;
    public bool NotifyRecommendations { get; set; } = true;
    public bool NotifyOptimizations   { get; set; } = false;

    // Onboarding
    public bool HasCompletedOnboarding { get; set; } = false;
    public string UsageProfile         { get; set; } = "";  // "Gaming", "Work / Productivity", "Mixed", "Content Creation"

    // Localization
    public string Language { get; set; } = "en-US";
}
