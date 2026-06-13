using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Undo grouping (divergence-7 fix): a profile apply opens an UndoService.BeginGroup scope so every
/// capture is stamped with the profile id, and RevertProfileAsync reverts exactly that group — so
/// two profiles bundling the same optimization no longer revert each other's captures.
/// </summary>
public class UndoGroupingTests
{
    [Fact]
    public void BeginGroup_stamps_groupId_until_disposed()
    {
        var undo = NewService();
        using (undo.BeginGroup("preset-clean"))
            undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "InGroup", "grouped");
        undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "OutOfGroup", "ungrouped");

        Assert.Equal("preset-clean", undo.Entries.First(e => e.ValueName == "InGroup").GroupId);
        Assert.Null(undo.Entries.First(e => e.ValueName == "OutOfGroup").GroupId);
    }

    [Fact]
    public void BeginGroup_restores_previous_group_on_dispose()
    {
        var undo = NewService();
        using (undo.BeginGroup("outer"))
        {
            using (undo.BeginGroup("inner"))
                undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "Inner", "inner");
            undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "Outer", "outer");
        }

        Assert.Equal("inner", undo.Entries.First(e => e.ValueName == "Inner").GroupId);
        Assert.Equal("outer", undo.Entries.First(e => e.ValueName == "Outer").GroupId);
    }

    [Fact]
    public async Task RevertProfileAsync_reverts_only_its_own_group_not_another_profiles_shared_optimization()
    {
        var undo = NewService();
        // Two profile applies that BOTH captured the SAME optimization id, each under its own group
        // (the exact situation the old OptimizationId-scoped revert got wrong).
        using (undo.BeginGroup("preset-gaming"))
            undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "Shared", "gaming", "OptShared");
        using (undo.BeginGroup("preset-performance"))
            undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "Shared", "perf", "OptShared");
        Assert.Equal(2, undo.Count);

        var svc = BuildService(undo);
        var reverted = await svc.RevertProfileAsync("preset-gaming");

        Assert.True(reverted);
        // The performance profile's capture for the SAME optimization id must SURVIVE.
        Assert.Single(undo.Entries);
        Assert.Equal("preset-performance", undo.Entries[0].GroupId);
    }

    [Fact]
    public async Task GroupId_flows_into_TaskRun_capture()
    {
        // The bundled-optimization captures run inside ApplyOptimizationAsync's Task.Run — this locks
        // in that the AsyncLocal group set on the calling flow flows into that worker (commit 96691ea).
        var undo = NewService();
        using (undo.BeginGroup("preset-x"))
            await Task.Run(() => undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", "InTask", "task"));

        Assert.Equal("preset-x", undo.Entries.First(e => e.ValueName == "InTask").GroupId);
    }

    [Fact]
    public async Task Concurrent_applies_do_not_cross_stamp_groups()
    {
        // Two apply flows hold their groups open SIMULTANEOUSLY (Barrier) — AsyncLocal must keep them
        // isolated; a plain field would let the second BeginGroup clobber the first.
        var undo = NewService();
        var barrier = new Barrier(2);

        // Each flow runs on its OWN pool thread (Task.Run returns immediately, so the test thread
        // never blocks at the barrier) — both can hold their BeginGroup scope open simultaneously,
        // proving AsyncLocal isolates them. The barrier has a timeout so a pool hiccup fails the
        // test fast instead of hanging it.
        Task Apply(string group, string valueName) => Task.Run(async () =>
        {
            using (undo.BeginGroup(group))
            {
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "both flows should reach the barrier");
                await Task.Run(() => undo.CaptureRegistry("HKCU", @"Software\Optimizer\GroupTest", valueName, group));
            }
        });

        await Task.WhenAll(Apply("A", "fromA"), Apply("B", "fromB"));

        Assert.Equal("A", undo.Entries.First(e => e.ValueName == "fromA").GroupId);
        Assert.Equal("B", undo.Entries.First(e => e.ValueName == "fromB").GroupId);
    }

    // Isolate every test on its own temp store so the suite never mutates the user's real undo.json.
    private static UndoService NewService() => new(Path.GetTempFileName());

    private static WindowsOptimizerService BuildService(IUndoService undo)
    {
        var loader = new Mock<IPluginLoader>();
        loader.Setup(l => l.CreateHandlers()).Returns(Array.Empty<IOptimizationHandler>());
        var startup = new Mock<IStartupService>();
        startup.Setup(s => s.GetEntries()).Returns(new List<StartupEntry>());
        return new WindowsOptimizerService(
            Array.Empty<IOptimizationHandler>(),
            loader.Object,
            undo,
            new Mock<IElevationService>().Object,
            new Mock<ISystemMonitorService>().Object,
            startup.Object,
            new Mock<IEventBus>().Object);
    }
}
