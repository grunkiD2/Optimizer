using System;
using System.Collections.Generic;
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
/// Safe-Tune destructive gate (audit 4b): a destructive optimization must be SKIPPED at the single
/// choke point on any headless/bundled surface (the default), and run only when a caller explicitly
/// opts in after an interactive confirmation. Skips are reported, never counted as failures.
/// </summary>
public class DestructiveGateTests
{
    private static Mock<IOptimizationHandler> Handler(string id, bool destructive)
    {
        var h = new Mock<IOptimizationHandler>();
        h.Setup(x => x.Id).Returns(id);
        h.Setup(x => x.Info).Returns(new OptimizationInfo { Id = id, Title = id, IsDestructive = destructive });
        h.Setup(x => x.ApplyAsync(It.IsAny<IUndoService>(), It.IsAny<IElevationService>()))
         .ReturnsAsync(new OptimizationResult { Success = true });
        return h;
    }

    private static WindowsOptimizerService BuildService(params IOptimizationHandler[] handlers)
    {
        var loader = new Mock<IPluginLoader>();
        loader.Setup(l => l.CreateHandlers()).Returns(Array.Empty<IOptimizationHandler>());
        var startup = new Mock<IStartupService>();
        startup.Setup(s => s.GetEntries()).Returns(new List<StartupEntry>());
        return new WindowsOptimizerService(
            handlers,
            loader.Object,
            new Mock<IUndoService>().Object,
            new Mock<IElevationService>().Object,
            new Mock<ISystemMonitorService>().Object,
            startup.Object,
            new Mock<IEventBus>().Object);
    }

    [Fact]
    public async Task Destructive_optimization_is_skipped_by_default()
    {
        var h = Handler("destructive-x", destructive: true);
        var svc = BuildService(h.Object);

        var r = await svc.ApplyOptimizationAsync("destructive-x");

        Assert.False(r.Success);
        Assert.True(r.Skipped);
        h.Verify(x => x.ApplyAsync(It.IsAny<IUndoService>(), It.IsAny<IElevationService>()), Times.Never);
    }

    [Fact]
    public async Task Destructive_optimization_runs_when_explicitly_included()
    {
        var h = Handler("destructive-x", destructive: true);
        var svc = BuildService(h.Object);

        var r = await svc.ApplyOptimizationAsync("destructive-x", includeDestructive: true);

        Assert.True(r.Success);
        Assert.False(r.Skipped);
        h.Verify(x => x.ApplyAsync(It.IsAny<IUndoService>(), It.IsAny<IElevationService>()), Times.Once);
    }

    [Fact]
    public async Task Nondestructive_optimization_is_unaffected_by_the_gate()
    {
        var h = Handler("safe-x", destructive: false);
        var svc = BuildService(h.Object);

        var r = await svc.ApplyOptimizationAsync("safe-x");   // headless default (includeDestructive: false)

        Assert.True(r.Success);
        Assert.False(r.Skipped);
        h.Verify(x => x.ApplyAsync(It.IsAny<IUndoService>(), It.IsAny<IElevationService>()), Times.Once);
    }

    [Fact]
    public void DisableStartupPrograms_is_marked_destructive()
    {
        var handler = new Optimizer.WinUI.Services.Optimizations.System.DisableStartupProgramsHandler();
        Assert.True(handler.Info.IsDestructive);
    }

    [Fact]
    public void ProfileApplyResult_reports_skipped_without_treating_it_as_failure()
    {
        var r = new ProfileApplyResult { Applied = 2, Failed = 0, Skipped = 1 };
        Assert.Contains("skipped", r.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(r.Success);   // a skipped destructive item is not a failure
    }
}
