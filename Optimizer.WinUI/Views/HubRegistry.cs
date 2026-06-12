using System;
using System.Collections.Generic;

namespace Optimizer.WinUI.Views;

/// <summary>One section within a hub — a label and the page shown when selected.</summary>
public sealed record HubSection(string Label, Type PageType);

/// <summary>A top-level destination in the slim rail: a title + its segmented sections.</summary>
public sealed record HubConfig(string Tag, string Title, IReadOnlyList<HubSection> Sections);

/// <summary>
/// Navigation target carrying a hub, the index of the section to select within that hub,
/// and an optional sub-section index for merged host pages (e.g. PerformancePage's inner
/// "Advanced Tuning" tab). Passed as the navigation parameter to <c>HubPage</c>.
/// </summary>
public sealed record HubNavTarget(HubConfig Hub, int SectionIndex, int? SubSectionIndex);

/// <summary>
/// The 4-hub information architecture from <c>docs/REDESIGN-IA.md</c>. Each hub is one
/// <see cref="HubPage"/> whose Segmented sub-nav swaps between the section pages. Individual
/// pages remain navigable directly (e.g. by the assistant) via MainWindow's PageMap — the
/// rail just groups them.
///
/// Section merges (each one host page absorbs another via an in-page Segmented switcher):
///   PerformancePage  → "CPU & Power"            (Performance + Tuning)
///   StartupPage      → "Startup & Services"     (Startup + Services)
/// </summary>
public static class HubRegistry
{
    public static readonly HubConfig Monitor = new("Monitor", "Monitor", new HubSection[]
    {
        new("Sensors & Inventory", typeof(HardwarePage)),
        new("Power Insights", typeof(PowerInsightsPage)),
        new("Event Log", typeof(EventLogsPage)),
    });

    public static readonly HubConfig Optimize = new("Optimize", "Optimize", new HubSection[]
    {
        new("CPU & Power", typeof(PerformancePage)),
        new("Privacy & System", typeof(SystemPage)),
        new("Network", typeof(NetworkPage)),
        new("Storage", typeof(StoragePage)),
        new("Startup & Services", typeof(StartupPage)),
        new("Devices", typeof(DevicesPage)),
    });

    public static readonly HubConfig Automate = new("Automate", "Automate", new HubSection[]
    {
        new("Profiles", typeof(ProfilesPage)),
        new("Recommendations", typeof(RecommendationsPage)),
        new("Learning", typeof(LearningPage)),
        new("History", typeof(HistoryPage)),
        new("Reports", typeof(ReportsPage)),
    });

    public static readonly HubConfig Protect = new("Protect", "Protect", new HubSection[]
    {
        new("Diagnostics", typeof(DiagnosticsPage)),
        new("Security", typeof(SecurityPage)),
        new("Updates", typeof(UpdatesPage)),
    });

    public static HubConfig? ByTag(string tag) => tag switch
    {
        "Monitor" => Monitor,
        "Optimize" => Optimize,
        "Automate" => Automate,
        "Protect" => Protect,
        _ => null,
    };
}

/// <summary>
/// Resolves an assistant-supplied navigation tag (current name OR pre-redesign back-compat
/// alias) into a <see cref="HubNavTarget"/>. Built once at startup; tested directly by
/// HubRoutingTests so the back-compat redirects don't silently rot when the IA shifts.
/// </summary>
public static class HubRouting
{
    private static readonly Dictionary<string, HubNavTarget> _routes = MakeRoutes();

    /// <summary>Try to resolve a tag. Returns null if the tag isn't a hub-section destination.</summary>
    public static HubNavTarget? Resolve(string tag)
        => _routes.TryGetValue(tag, out var t) ? t : null;

    /// <summary>All tags this resolver knows. Exposed so PageNavigator can advertise them to the AI.</summary>
    public static IEnumerable<string> KnownTags => _routes.Keys;

    private static Dictionary<string, HubNavTarget> MakeRoutes()
    {
        static HubNavTarget Mk(HubConfig hub, string sectionLabel, int? sub = null)
        {
            var idx = 0;
            for (int i = 0; i < hub.Sections.Count; i++)
                if (string.Equals(hub.Sections[i].Label, sectionLabel, StringComparison.Ordinal))
                { idx = i; break; }
            return new HubNavTarget(hub, idx, sub);
        }
        return new(StringComparer.OrdinalIgnoreCase)
        {
            // Monitor
            ["Hardware"]        = Mk(HubRegistry.Monitor, "Sensors & Inventory"),
            ["PowerInsights"]   = Mk(HubRegistry.Monitor, "Power Insights"),
            ["EventLogs"]       = Mk(HubRegistry.Monitor, "Event Log"),

            // Optimize
            ["Performance"]     = Mk(HubRegistry.Optimize, "CPU & Power"),
            ["CpuAndPower"]     = Mk(HubRegistry.Optimize, "CPU & Power"),
            ["System"]          = Mk(HubRegistry.Optimize, "Privacy & System"),
            ["Network"]         = Mk(HubRegistry.Optimize, "Network"),
            ["Storage"]         = Mk(HubRegistry.Optimize, "Storage"),
            ["Startup"]         = Mk(HubRegistry.Optimize, "Startup & Services"),
            ["Devices"]         = Mk(HubRegistry.Optimize, "Devices"),

            // Automate
            ["Profiles"]        = Mk(HubRegistry.Automate, "Profiles"),
            ["Recommendations"] = Mk(HubRegistry.Automate, "Recommendations"),
            ["Learning"]        = Mk(HubRegistry.Automate, "Learning"),
            ["History"]         = Mk(HubRegistry.Automate, "History"),
            ["Reports"]         = Mk(HubRegistry.Automate, "Reports"),

            // Protect
            ["Diagnostics"]     = Mk(HubRegistry.Protect, "Diagnostics"),
            ["Security"]        = Mk(HubRegistry.Protect, "Security"),
            ["Updates"]         = Mk(HubRegistry.Protect, "Updates"),

            // ── Back-compat: tags that pre-IA-redesign were standalone pages now resolve
            //    to the merged host's inner Segmented panel. Sub-section index matches the
            //    Segmented item declared in the host page's XAML.
            ["Tuning"]          = Mk(HubRegistry.Optimize, "CPU & Power",        sub: 1),
            ["Services"]        = Mk(HubRegistry.Optimize, "Startup & Services", sub: 1),
        };
    }
}
