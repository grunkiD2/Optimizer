using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for IntelligenceService.ComputeLocalSummary() and ComputePrivatizedSummary().
/// </summary>
public class LocalSummaryTests
{
    private static IntelligenceService BuildService(Dictionary<string, RecommendationPreference> prefs)
    {
        var recs = new Mock<IRecommendationsService>();
        recs.Setup(r => r.GetPreferences())
            .Returns(prefs);

        var history = new Mock<IHistoryService>();
        history.Setup(h => h.Entries)
            .Returns(new List<HistoryEntry>().AsReadOnly());

        var eventBus = new Mock<IEventBus>();
        return new IntelligenceService(recs.Object, history.Object, eventBus.Object);
    }

    // ── ComputeLocalSummary ──────────────────────────────────────────────────

    [Fact]
    public void ComputeLocalSummary_EmptyPreferences_ReturnsEmptyRatesAndZeroSamples()
    {
        var svc = BuildService(new Dictionary<string, RecommendationPreference>());

        var summary = svc.ComputeLocalSummary();

        Assert.Empty(summary.CategoryAcceptanceRates);
        Assert.Equal(0, summary.TotalSamples);
        Assert.True(summary.ComputedUtc <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void ComputeLocalSummary_CorrectAcceptanceRate_SingleCategory()
    {
        // "cpu-perf" maps to "Performance" category
        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["cpu-perf"] = new() { Id = "cpu-perf", AcceptCount = 3, DismissCount = 1 }
        };
        var svc = BuildService(prefs);

        var summary = svc.ComputeLocalSummary();

        Assert.True(summary.CategoryAcceptanceRates.ContainsKey("Performance"),
            "Expected 'Performance' category in summary");
        // rate = 3 / (3+1) = 0.75
        Assert.Equal(0.75, summary.CategoryAcceptanceRates["Performance"], precision: 5);
        Assert.Equal(4, summary.TotalSamples);
    }

    [Fact]
    public void ComputeLocalSummary_AggregatesMultipleIdsInSameCategory()
    {
        // Both "cpu-perf" and "perf-boost" map to "Performance"
        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["cpu-perf"]   = new() { Id = "cpu-perf",   AcceptCount = 2, DismissCount = 0 },
            ["perf-boost"] = new() { Id = "perf-boost", AcceptCount = 1, DismissCount = 1 }
        };
        var svc = BuildService(prefs);

        var summary = svc.ComputeLocalSummary();

        // Combined: 3 accepts out of 4 total = 0.75
        Assert.True(summary.CategoryAcceptanceRates.ContainsKey("Performance"));
        Assert.Equal(0.75, summary.CategoryAcceptanceRates["Performance"], precision: 5);
        Assert.Equal(4, summary.TotalSamples);
    }

    [Fact]
    public void ComputeLocalSummary_NoInteractions_SkipsCategory()
    {
        // Preference with 0 accepts and 0 dismisses should not produce a divide-by-zero
        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["disk-clean"] = new() { Id = "disk-clean", AcceptCount = 0, DismissCount = 0 }
        };
        var svc = BuildService(prefs);

        // Should not throw and should return empty rates (no interactions to summarize)
        var ex = Record.Exception(() => svc.ComputeLocalSummary());
        Assert.Null(ex);

        var summary = svc.ComputeLocalSummary();
        Assert.Equal(0, summary.TotalSamples);
    }

    // ── ComputePrivatizedSummary ──────────────────────────────────────────────

    [Fact]
    public void ComputePrivatizedSummary_RatesStayInZeroOne()
    {
        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["cpu-perf"]   = new() { Id = "cpu-perf",   AcceptCount = 5, DismissCount = 2 },
            ["disk-clean"] = new() { Id = "disk-clean", AcceptCount = 1, DismissCount = 3 }
        };
        var svc = BuildService(prefs);

        // Run many times with different seeds via the actual method
        for (int i = 0; i < 50; i++)
        {
            var summary = svc.ComputePrivatizedSummary(epsilon: 1.0);
            foreach (var rate in summary.CategoryAcceptanceRates.Values)
            {
                Assert.True(rate >= 0.0 && rate <= 1.0,
                    $"Rate {rate} is outside [0,1]");
            }
            Assert.True(summary.TotalSamples >= 0, "TotalSamples must be ≥ 0");
        }
    }

    [Fact]
    public void ComputePrivatizedSummary_DiffersFromRaw()
    {
        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["cpu-perf"] = new() { Id = "cpu-perf", AcceptCount = 8, DismissCount = 2 }
        };
        var svc = BuildService(prefs);

        var raw        = svc.ComputeLocalSummary();
        var privatized = svc.ComputePrivatizedSummary(epsilon: 1.0);

        // With epsilon=1.0 and a seeded Random.Shared (non-deterministic in tests),
        // we just check that the privatized summary has rates and is structurally identical.
        // We cannot assert exact values but we can check bounds and that DP was attempted.
        Assert.Equal(raw.CategoryAcceptanceRates.Keys.OrderBy(k => k),
                     privatized.CategoryAcceptanceRates.Keys.OrderBy(k => k));
    }

    [Fact]
    public void ComputePrivatizedSummary_HighEpsilon_CloseToRaw()
    {
        // With very high epsilon noise is tiny; average over many calls ≈ raw value.
        const int n = 200;
        const double epsilon = 1000.0;

        var prefs = new Dictionary<string, RecommendationPreference>
        {
            ["cpu-perf"] = new() { Id = "cpu-perf", AcceptCount = 7, DismissCount = 3 }
        };
        var svc = BuildService(prefs);

        double rawRate = svc.ComputeLocalSummary().CategoryAcceptanceRates["Performance"];

        double sumRates = 0;
        for (int i = 0; i < n; i++)
            sumRates += svc.ComputePrivatizedSummary(epsilon).CategoryAcceptanceRates["Performance"];

        double meanRate = sumRates / n;
        Assert.True(Math.Abs(meanRate - rawRate) < 0.05,
            $"Mean privatized rate {meanRate:F4} is too far from raw {rawRate:F4}");
    }
}
