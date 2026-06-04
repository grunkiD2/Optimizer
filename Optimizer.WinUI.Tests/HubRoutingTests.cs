using System;
using System.Collections.Generic;
using System.Linq;
using Optimizer.WinUI.Views;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Bug C regression — when the AI calls navigate_to_page("Tuning"), HubRouting must resolve
/// the tag to the Optimize hub's "CPU &amp; Power" section AND PerformancePage's "Advanced
/// Tuning" inner Segmented panel (sub-section index 1). Same for the other three merged
/// hosts. If the IA shifts again and a section is renamed without updating HubRouting,
/// these tests fail loudly.
/// </summary>
public class HubRoutingTests
{
    [Theory]
    // Back-compat: pre-redesign standalone tags now route to merged sub-sections
    [InlineData("Tuning",      "Optimize", "CPU & Power",        1)]
    [InlineData("Services",    "Optimize", "Startup & Services", 1)]
    [InlineData("Plugins",     "Extend",   "Extensions",         1)]
    [InlineData("Marketplace", "Extend",   "Extensions",         0)]
    [InlineData("Templates",   "Automate", "Profiles",           1)]
    public void BackCompat_tags_route_to_correct_hub_section_and_subsection(
        string tag, string expectedHubTag, string expectedSectionLabel, int expectedSubSection)
    {
        var target = HubRouting.Resolve(tag);

        Assert.NotNull(target);
        Assert.Equal(expectedHubTag, target!.Hub.Tag);
        Assert.Equal(expectedSectionLabel, target.Hub.Sections[target.SectionIndex].Label);
        Assert.Equal(expectedSubSection, target.SubSectionIndex);
    }

    [Theory]
    // Current-IA tags route to the hub section with no sub-section hint
    [InlineData("Performance", "Optimize", "CPU & Power")]
    [InlineData("CpuAndPower", "Optimize", "CPU & Power")]
    [InlineData("System",      "Optimize", "Privacy & System")]
    [InlineData("Storage",     "Optimize", "Storage")]
    [InlineData("Hardware",    "Monitor",  "Sensors & Inventory")]
    [InlineData("EventLogs",   "Monitor",  "Event Log")]
    [InlineData("Profiles",    "Automate", "Profiles")]
    [InlineData("Updates",     "Protect",  "Updates")]
    [InlineData("Extensions",  "Extend",   "Extensions")]
    public void Current_tags_route_to_hub_section_with_no_subsection(
        string tag, string expectedHubTag, string expectedSectionLabel)
    {
        var target = HubRouting.Resolve(tag);

        Assert.NotNull(target);
        Assert.Equal(expectedHubTag, target!.Hub.Tag);
        Assert.Equal(expectedSectionLabel, target.Hub.Sections[target.SectionIndex].Label);
        Assert.Null(target.SubSectionIndex);
    }

    [Fact]
    public void Tag_lookup_is_case_insensitive()
    {
        Assert.NotNull(HubRouting.Resolve("tuning"));
        Assert.NotNull(HubRouting.Resolve("TUNING"));
        Assert.NotNull(HubRouting.Resolve("TuNiNg"));
    }

    [Fact]
    public void Unknown_tag_returns_null()
    {
        Assert.Null(HubRouting.Resolve("NotARealPage"));
        Assert.Null(HubRouting.Resolve(""));
    }

    [Fact]
    public void Known_tags_set_advertised_to_assistant_includes_all_routes()
    {
        var tags = HubRouting.KnownTags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every back-compat alias must be reachable through KnownTags so the AI can still
        // navigate via the old name even after the IA redesign.
        Assert.Contains("Tuning", tags);
        Assert.Contains("Services", tags);
        Assert.Contains("Plugins", tags);
        Assert.Contains("Templates", tags);

        // The current names must also be reachable.
        Assert.Contains("CpuAndPower", tags);
        Assert.Contains("Extensions", tags);
    }
}
