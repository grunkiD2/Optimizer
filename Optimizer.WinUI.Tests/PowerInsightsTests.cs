using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Optimizer.WinUI.Services.Power;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class DriftDetectorTests
{
    [Fact]
    public void Learning_until_min_samples_then_classifies()
    {
        var b = new PowerBaseline();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 19; i++) DriftDetector.Update(b, 5.0, now.AddSeconds(i), halfLifeHours: 72);
        Assert.Equal(DriftClass.Learning, DriftDetector.Classify(b, 50, zThreshold: 3.5));

        // Two more updates: decay nibbles fractions off Count, so exactly 20 lands at ~19.9998.
        DriftDetector.Update(b, 5.0, now.AddSeconds(19), 72);
        DriftDetector.Update(b, 5.0, now.AddSeconds(20), 72);
        Assert.Equal(DriftClass.Normal, DriftDetector.Classify(b, 5.2, 3.5));
        Assert.Equal(DriftClass.Anomalous, DriftDetector.Classify(b, 50, 3.5));
    }

    [Fact]
    public void Welford_mean_and_sigma_match_closed_form()
    {
        var b = new PowerBaseline();
        var now = DateTimeOffset.Now;
        double[] xs = [4, 6, 5, 7, 3, 5, 6, 4];
        foreach (var (x, i) in xs.Select((x, i) => (x, i)))
            DriftDetector.Update(b, x, now.AddSeconds(i), halfLifeHours: 0); // 0 = no decay branch
        Assert.Equal(xs.Average(), b.MeanW, 10);
        var variance = xs.Select(x => Math.Pow(x - xs.Average(), 2)).Sum() / xs.Length;
        Assert.Equal(Math.Sqrt(variance), b.SigmaW, 10);
    }

    [Fact]
    public void Sigma_floor_prevents_screaming_on_steady_processes()
    {
        var b = new PowerBaseline();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 30; i++) DriftDetector.Update(b, 5.0, now.AddSeconds(i), 72); // σ = 0
        // +1 W over a perfectly steady 5 W process: z would be ∞ without the floor.
        Assert.Equal(DriftClass.Normal, DriftDetector.Classify(b, 6.0, 3.5));
        Assert.Equal(DriftClass.Anomalous, DriftDetector.Classify(b, 5.0 + 0.75 * 3.5 + 0.1, 3.5));
    }

    [Fact]
    public void Half_life_decay_lets_baseline_re_learn()
    {
        var b = new PowerBaseline();
        var t0 = DateTimeOffset.Now;
        for (var i = 0; i < 100; i++) DriftDetector.Update(b, 5.0, t0.AddSeconds(i), halfLifeHours: 72);
        var countBefore = b.Count;

        // 72 h later the effective sample size has halved — one new observation at a new
        // level moves the mean MORE than it would have without decay.
        DriftDetector.Update(b, 15.0, t0.AddHours(72), halfLifeHours: 72);
        Assert.True(b.Count < countBefore * 0.6, $"decay should have halved Count (was {countBefore}, now {b.Count})");
        Assert.True(b.MeanW > 5.15, $"mean should move noticeably after decay (got {b.MeanW:F3})");
    }

    [Fact]
    public void Drift_is_one_sided_low_power_is_never_anomalous()
    {
        var b = new PowerBaseline();
        var now = DateTimeOffset.Now;
        for (var i = 0; i < 30; i++) DriftDetector.Update(b, 20.0, now.AddSeconds(i), 72);
        Assert.Equal(DriftClass.Normal, DriftDetector.Classify(b, 0.1, 3.5)); // dropping power = fine
    }
}

public class PowerAttributionTests
{
    private static Dictionary<(int, long), (string, TimeSpan)> Procs(params (int pid, string name, double cpuSec)[] items)
        => items.ToDictionary(i => (i.pid, 0L), i => (i.name, TimeSpan.FromSeconds(i.cpuSec)));

    [Fact]
    public void Attribution_splits_package_watts_by_cpu_share_and_sums_to_attributed_pool()
    {
        var prev = Procs((1, "game", 0), (2, "chrome", 0), (3, "idleApp", 0));
        var cur  = Procs((1, "game", 24), (2, "chrome", 8), (3, "idleApp", 0));
        var snap = PowerAttributionService.Attribute(DateTimeOffset.Now, windowSeconds: 10, packageWatts: 100,
            prev, cur, processorCount: 32);

        // 32 cores × 10 s = 320 capacity-seconds; 32 busy → attributedShare = 0.1
        Assert.Equal(0.1, snap.AttributedShare, 3);
        var game = snap.Processes.Single(p => p.Name == "game");
        var chrome = snap.Processes.Single(p => p.Name == "chrome");
        Assert.Equal(7.5, game.EstimatedWatts, 3);    // 100 W × 0.1 × (24/32)
        Assert.Equal(2.5, chrome.EstimatedWatts, 3);
        // Σ(process W) == packageWatts × attributedShare — the sum-to-system invariant.
        Assert.Equal(100 * snap.AttributedShare, snap.Processes.Sum(p => p.EstimatedWatts), 6);
        Assert.DoesNotContain(snap.Processes, p => p.Name == "idleApp"); // zero delta → dropped
    }

    [Fact]
    public void First_seen_processes_are_skipped_until_next_round()
    {
        var prev = Procs((1, "old", 0));
        var cur  = Procs((1, "old", 5), (99, "fresh", 1000)); // fresh has huge LIFETIME cpu
        var snap = PowerAttributionService.Attribute(DateTimeOffset.Now, 10, 100, prev, cur, 32);
        Assert.DoesNotContain(snap.Processes, p => p.Name == "fresh");
    }

    [Fact]
    public void Same_name_instances_are_grouped_with_instance_count()
    {
        var prev = Procs((1, "chrome", 0), (2, "chrome", 0));
        var cur  = Procs((1, "chrome", 4), (2, "chrome", 6));
        var snap = PowerAttributionService.Attribute(DateTimeOffset.Now, 10, 50, prev, cur, 32);
        var chrome = snap.Processes.Single();
        Assert.Equal(2, chrome.InstanceCount);
        Assert.Equal(10, chrome.CpuSeconds, 3);
    }

    [Fact]
    public void Missing_package_watts_yields_zero_estimates_but_keeps_shares()
    {
        var prev = Procs((1, "x", 0));
        var cur  = Procs((1, "x", 5));
        var snap = PowerAttributionService.Attribute(DateTimeOffset.Now, 10, packageWatts: null, prev, cur, 32);
        Assert.Equal(0, snap.Processes.Single().EstimatedWatts);
        Assert.True(snap.Processes.Single().CpuShare > 0);
    }

    [Fact]
    public void Exclusion_regexes_compile_and_invalid_patterns_are_skipped()
    {
        var list = PowerInsightsService.CompileExclusions(["MsMpEng", "[invalid", @"Optimizer\.WinUI", " "]);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, rx => rx.IsMatch("msmpeng"));
        Assert.Contains(list, rx => rx.IsMatch("Optimizer.WinUI"));
    }
}

/// <summary>POWER-INSIGHTS verification check #8: PPI is read-only by contract.
/// Source-level audit — the Power services must never write registry, kill processes,
/// change priorities/affinities, or call powercfg.</summary>
public class PpiReadOnlyTests
{
    [Fact]
    public void Power_services_source_contains_no_mutating_calls()
    {
        var dir = Path.Combine(FindRepoRoot(), "Optimizer.WinUI", "Services", "Power");
        var sources = Directory.GetFiles(dir, "*.cs").Select(File.ReadAllText).ToList();
        Assert.NotEmpty(sources);
        string[] forbidden = [".Kill(", "SetValue(", "PriorityClass", "ProcessorAffinity", "powercfg", "SetPriorityClass"];
        foreach (var src in sources)
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, src, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Optimizer.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
