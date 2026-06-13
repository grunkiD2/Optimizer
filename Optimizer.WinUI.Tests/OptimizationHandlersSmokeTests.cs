using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Optimizations;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Stability tests covering every <see cref="IOptimizationHandler"/> in the app. The
/// motivating bug: I shipped the WinUI app 2026-06-04 with 5 new handlers, didn't write
/// per-handler tests, and only noticed the app crashed at startup because a stray
/// background pwsh was holding port 8765. A simple "every handler constructs" test would
/// have caught any handler-ctor regression before launch.
///
/// Each test in this file applies to EVERY handler in the assembly (system-wide, per the
/// "fix everywhere" feedback memory). Adding a new handler tomorrow without changing
/// these tests still exercises the new code automatically.
/// </summary>
public class OptimizationHandlersSmokeTests
{
    public static IEnumerable<object[]> AllHandlerTypes()
    {
        var handlerInterface = typeof(IOptimizationHandler);
        var assembly = handlerInterface.Assembly;
        var concreteHandlers = assembly.GetTypes()
            .Where(t => !t.IsAbstract
                     && !t.IsInterface
                     && handlerInterface.IsAssignableFrom(t)
                     // Skip the plugin-supplied wrapper (it takes ctor args we can't synthesize here)
                     && t.Name != "ManifestOptimizationHandler");

        foreach (var t in concreteHandlers)
            yield return new object[] { t };
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handler_constructs_without_throwing(Type handlerType)
    {
        // Built-in handlers all use a parameterless ctor — they're registered as
        // transient services and DI resolves them by activating directly.
        var instance = Activator.CreateInstance(handlerType);
        Assert.NotNull(instance);
        Assert.IsAssignableFrom<IOptimizationHandler>(instance);
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handler_exposes_non_empty_metadata(Type handlerType)
    {
        var handler = (IOptimizationHandler)Activator.CreateInstance(handlerType)!;

        // The optimization-card UI binds to these — null or empty would render an
        // unusable card on the relevant hub page.
        Assert.NotNull(handler.Id);
        Assert.NotEmpty(handler.Id);
        Assert.NotNull(handler.Info);
        Assert.NotEmpty(handler.Info.Title);
        Assert.NotEmpty(handler.Info.Summary);

        // Id must agree with Info.Id (drift would break undo / analytics linkage).
        Assert.Equal(handler.Id, handler.Info.Id);
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handler_must_inherit_OptimizationHandlerBase(Type handlerType)
    {
        // Architectural rule. The base class wraps every registry write through
        // IUndoService.CaptureRegistry — bypassing the base means optimizations become
        // irreversible silently. Catch the drift at test time, not on the user's machine.
        Assert.True(typeof(OptimizationHandlerBase).IsAssignableFrom(handlerType),
            $"{handlerType.Name} implements IOptimizationHandler directly. " +
            "Every handler must inherit OptimizationHandlerBase so undo capture is honoured.");
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public async Task Handler_that_requires_admin_returns_NotElevated_when_unelevated(Type handlerType)
    {
        var handler = (IOptimizationHandler)Activator.CreateInstance(handlerType)!;
        if (!handler.Info.RequiresAdmin) return; // not applicable

        var undo = new FakeUndoService();
        var elevation = new FakeElevation(isElevated: false);

        var result = await handler.ApplyAsync(undo, elevation);

        // Either: the handler explicitly returns Success=false with an admin-needed
        // message, OR it threw inside ApplyAsync and surfaced an error. Both are
        // acceptable — what we don't want is a silent success that does nothing or a
        // partial write that the user can't undo.
        Assert.False(result.Success,
            $"{handlerType.Name}.Info.RequiresAdmin is true but ApplyAsync succeeded without elevation. " +
            "Honor the elevation gate or set RequiresAdmin=false.");
        Assert.Empty(undo.Captures); // no half-applied state
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handler_Info_lists_at_least_one_concrete_change(Type handlerType)
    {
        var handler = (IOptimizationHandler)Activator.CreateInstance(handlerType)!;
        Assert.NotEmpty(handler.Info.Changes);
    }

    // ── Test doubles ──────────────────────────────────────────────────────

    private sealed class FakeUndoService : IUndoService
    {
        public List<(string Root, string SubKey, string ValueName)> Captures { get; } = new();
        private readonly List<UndoEntry> _entries = new();
        public int Count => _entries.Count;
        public IReadOnlyList<UndoEntry> Entries => _entries;
        public void CaptureRegistry(string root, string subKey, string valueName, string description, string? optimizationId = null)
        {
            Captures.Add((root, subKey, valueName));
            _entries.Add(new UndoEntry { Kind = UndoActionKind.RegistryValue, RegistryRoot = root, SubKey = subKey, ValueName = valueName, Description = description, OptimizationId = optimizationId });
        }
        public void CapturePowerScheme(string previousGuid, string description, string? optimizationId = null) { }
        public Task<int> UndoAllAsync() => Task.FromResult(0);
        public Task<bool> UndoAsync(UndoEntry entry) => Task.FromResult(true);
        public void Load() { }
        public Task SaveAsync() => Task.CompletedTask;
    }

    private sealed class FakeElevation : IElevationService
    {
        public FakeElevation(bool isElevated) { IsElevated = isElevated; }
        public bool IsElevated { get; }
        public bool TryRelaunchElevated() => false;
    }
}
