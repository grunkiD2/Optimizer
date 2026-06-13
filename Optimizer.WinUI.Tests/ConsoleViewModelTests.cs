using System;
using System.Collections.Generic;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

[Collection("EngineLog")]
public class ConsoleViewModelTests
{
    private sealed class FakeBus : IEventBus
    {
        private readonly List<Action<OptimizerEvent>> _subs = [];
        public List<OptimizerEvent> Recent { get; } = [];
        public IReadOnlyList<OptimizerEvent> RecentEvents => Recent;
        public void Publish(OptimizerEvent evt) { foreach (var s in _subs) s(evt); }
        public IDisposable Subscribe(Action<OptimizerEvent> h) { _subs.Add(h); return new Noop(); }
        public IDisposable Subscribe(OptimizerEventType t, Action<OptimizerEvent> h)
            => Subscribe(e => { if (e.Type == t) h(e); });
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void Seeds_from_recent_events_on_construction()
    {
        var bus = new FakeBus();
        bus.Recent.Add(OptimizerEvent.Create(OptimizerEventType.ProfileApplied, "Applied Gaming", "ok"));
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        // ConsoleViewModel also subscribes to the static EngineLog bus, so parallel tests
        // can leak lines into Lines — including lines with SIMILAR text (handler smoke
        // tests log via EngineLog). Assert presence, never count.
        Assert.Contains(vm.Lines, l => l.Text.Contains("Applied Gaming"));
    }

    [Fact]
    public void Appends_a_line_when_an_event_is_published()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "Disabled telemetry", "done"));
        Assert.Contains(vm.Lines, l => l.Text.Contains("Disabled telemetry"));
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "x", "y"));
        vm.ClearCommand.Execute(null);
        Assert.DoesNotContain(vm.Lines, l => l.Text.Contains("x — y"));
    }
}
