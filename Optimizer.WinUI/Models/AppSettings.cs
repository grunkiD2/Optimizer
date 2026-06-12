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

    // Launch maximized (fills the screen, keeps the title bar/window controls). Default ON.
    public bool StartMaximized { get; set; } = true;

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

    // Console: when true, the Activity console mirrors all engine log output
    // (not just optimization/event-bus events). Defaults ON so the console shows everything.
    public bool VerboseConsole { get; set; } = true;

    // External sensor source: when set, sensors are read from a LibreHardwareMonitor
    // web server's /data.json endpoint instead of initializing LHM in-process. Use when
    // another component on the machine already owns the LHM kernel driver (see
    // docs/MACHINE-OWNERSHIP.md). Empty = in-process LHM (default). No silent fallback:
    // if the server is down, sensors stay unavailable until it returns.
    public string ExternalSensorServerUrl { get; set; } = "";

    // Remote API
    public bool ApiEnabled { get; set; } = false;
    public int ApiPort { get; set; } = 8765;
    public string ApiToken { get; set; } = Guid.NewGuid().ToString();

    // Cloud Sync
    public bool CloudSyncEnabled { get; set; } = false;
    public string CloudServerUrl { get; set; } = "http://localhost:5000";

    // Privacy-Preserving Community Insights (Federated Learning scaffold)
    // Default is FALSE — strictly opt-in. When enabled, only differentially-private
    // aggregated statistics are shared. Raw preferences or system data are NEVER uploaded.
    public bool FederatedLearningEnabled { get; set; } = false;

    // AI Assistant (Claude API — opt-in, bring-your-own-key; the key itself is stored
    // separately, encrypted via DPAPI, never in this settings file).
    public bool AssistantEnabled { get; set; } = false;
    public bool AssistantAllowActions { get; set; } = true;
    public string AssistantModel { get; set; } = "claude-sonnet-4-6";

    // When the app is running elevated, skip the per-tool-call confirmation dialog the
    // assistant raises before running mutating commands. Reasoning: the user already
    // granted admin rights to the whole process at launch (UAC); a second prompt per
    // command is friction without a security benefit. Defaults ON to match user
    // expectation; flip off in Settings if you want per-item confirmations.
    public bool AssistantAutoConfirmWhenElevated { get; set; } = true;

    // ── Phase 7: Autonomous automation (all opt-in, all reversible) ──
    // Master kill switch — when true, ALL automation is paused regardless of the toggles below.
    public bool AutomationPaused { get; set; } = false;

    // Auto-switch to the best-known profile when the detected context changes.
    public bool AutoContextSwitchEnabled { get; set; } = false;

    // Minimum confidence (success-rate × volume, 0..1) required to auto-switch.
    public double AutoContextSwitchConfidence { get; set; } = 0.7;

    // Auto-apply "safe" optimizations that have already succeeded repeatedly in a context
    // (confirm-on-first-occurrence: the first applications still require the user).
    public bool AutoApplyEnabled { get; set; } = false;

    // Number of prior successes in a context before an optimization may auto-apply.
    public int AutoApplySuccessThreshold { get; set; } = 3;

    // Optimization ids the user never wants auto-applied.
    public List<string> AutoApplyExcluded { get; set; } = new();
}
