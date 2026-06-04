using System;
using System.Collections.Generic;
using System.Linq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Views;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Catches the class of bug where a switch / dictionary over an enum silently lets unmapped
/// values fall through to a generic fallback. The motivating case: <see cref="CommandCenterPage.CategoryRoutes"/>
/// originally handled 4 of 8 <see cref="FindingCategory"/> values; the other 4 silently routed
/// to "Recommendations". Unit tests on each method passed; the bug only surfaced in code review.
///
/// New enum-keyed routing tables should be wired up here. If you add a value to the enum,
/// these tests fail until you map it.
/// </summary>
public class EnumExhaustivenessTests
{
    /// <summary>Every <see cref="FindingCategory"/> value must have an explicit mapping in
    /// <see cref="CommandCenterPage.CategoryRoutes"/>.</summary>
    [Fact]
    public void CategoryRoutes_Covers_Every_FindingCategory()
    {
        var allValues = Enum.GetValues<FindingCategory>();
        var mapped = CommandCenterPage.CategoryRoutes.Keys.ToHashSet();
        var missing = allValues.Where(v => !mapped.Contains(v)).ToList();

        Assert.True(missing.Count == 0,
            $"CategoryRoutes is missing mappings for: {string.Join(", ", missing)}. " +
            "Add an entry to CommandCenterPage.CategoryRoutes — silent fall-through to " +
            "\"Recommendations\" was the original bug shape and we don't want to relive it.");
    }

    /// <summary>Every mapped tag must resolve via <see cref="HubRouting"/> — otherwise navigation
    /// will fall through to standalone-page mode, defeating the hub-aware routing.</summary>
    [Fact]
    public void CategoryRoutes_Tags_Are_All_KnownTags()
    {
        var known = HubRouting.KnownTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bad = CommandCenterPage.CategoryRoutes
            .Where(kv => !known.Contains(kv.Value))
            .ToList();

        Assert.True(bad.Count == 0,
            $"CategoryRoutes maps to unknown tag(s): {string.Join(", ", bad.Select(kv => $"{kv.Key}→{kv.Value}"))}. " +
            "Tags must appear in HubRouting._routes so IPageNavigator.NavigateTo resolves to a hub.");
    }
}
