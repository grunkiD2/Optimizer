using System;
using System.Collections.Generic;

namespace Optimizer.WinUI.Views;

/// <summary>One section within a hub — a label and the page shown when selected.</summary>
public sealed record HubSection(string Label, Type PageType);

/// <summary>A top-level destination in the slim rail: a title + its segmented sections.</summary>
public sealed record HubConfig(string Tag, string Title, IReadOnlyList<HubSection> Sections);

/// <summary>
/// The 5-hub information architecture. Each hub is one <see cref="HubPage"/> whose Segmented
/// sub-nav swaps between the section pages. Individual pages remain navigable directly (e.g. by the
/// assistant) via MainWindow's PageMap — the rail just groups them.
/// </summary>
public static class HubRegistry
{
    public static readonly HubConfig Monitor = new("Monitor", "Monitor", new HubSection[]
    {
        new("Sensors & Inventory", typeof(HardwarePage)),
        new("Devices", typeof(DevicesPage)),
        new("Event Log", typeof(EventLogsPage)),
        new("Updates", typeof(UpdatesPage)),
    });

    public static readonly HubConfig Optimize = new("Optimize", "Optimize", new HubSection[]
    {
        // "CPU & Power" merges what used to be Performance + Tuning into one page
        // (PerformancePage hosts both ViewModels and an in-page Segmented switcher).
        new("CPU & Power", typeof(PerformancePage)),
        new("Privacy & System", typeof(SystemPage)),
        new("Network", typeof(NetworkPage)),
        new("Storage", typeof(StoragePage)),
        // "Startup & Services" merges what used to be Startup + Services — one place
        // for "what runs at boot / in the background." Hosted by StartupPage.
        new("Startup & Services", typeof(StartupPage)),
    });

    public static readonly HubConfig Automate = new("Automate", "Automate", new HubSection[]
    {
        new("Profiles", typeof(ProfilesPage)),
        new("Recommendations", typeof(RecommendationsPage)),
        new("Learning", typeof(LearningPage)),
        new("History", typeof(HistoryPage)),
        new("Templates", typeof(TemplatesPage)),
    });

    public static readonly HubConfig Protect = new("Protect", "Protect", new HubSection[]
    {
        new("Diagnostics", typeof(DiagnosticsPage)),
        new("Security", typeof(SecurityPage)),
        new("Compliance", typeof(CompliancePage)),
    });

    public static readonly HubConfig Extend = new("Extend", "Extend", new HubSection[]
    {
        new("Marketplace", typeof(MarketplacePage)),
        new("Plugins", typeof(PluginsPage)),
        new("Fleet", typeof(FleetPage)),
        new("Reports", typeof(ReportsPage)),
    });

    public static HubConfig? ByTag(string tag) => tag switch
    {
        "Monitor" => Monitor,
        "Optimize" => Optimize,
        "Automate" => Automate,
        "Protect" => Protect,
        "Extend" => Extend,
        _ => null,
    };
}
