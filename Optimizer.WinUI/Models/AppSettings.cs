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

    // Fancontrol federation (read-only): when set, points at the Fancontrol system's
    // state directory (e.g. L:\Users\Fancontrol\state) and Optimizer surfaces its
    // brain/profile/sentinel status in the dashboard + /api/fancontrol. Optimizer
    // never WRITES anything there — see docs/MACHINE-OWNERSHIP.md.
    public string FancontrolStateDir { get; set; } = "";

    // Profil 2.0 P2.0-d: when true, the active Fancontrol profile's "optimizer" preset-link is applied
    // automatically (with undo, EngineLog only) when the profile changes. Opt-in / ship-dark; read live
    // each poll so the toggle takes effect without a restart. Needs FancontrolStateDir set to do anything.
    public bool FancontrolFollowerEnabled { get; set; } = false;

    // ── Per-Process Power Intelligence (docs/POWER-INSIGHTS.md) — read-only,
    // suggestions only. Off by default until shaken out (ship-dark policy).
    public bool PpiEnabled { get; set; } = false;
    public bool PpiSuggestionsEnabled { get; set; } = true;
    public double PpiDriftZThreshold { get; set; } = 3.5;
    public double PpiBaselineHalfLifeHours { get; set; } = 72;
    public List<string> PpiProcessExclusions { get; set; } =
        ["MsMpEng", "TrustedInstaller", "SearchHost", "SystemSettings", @"Optimizer\.WinUI"];

    // Remote API
    public bool ApiEnabled { get; set; } = false;
    public int ApiPort { get; set; } = 8765;
    public string ApiToken { get; set; } = Guid.NewGuid().ToString();

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
