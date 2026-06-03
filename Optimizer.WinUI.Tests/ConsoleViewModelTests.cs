using System;
using System.Collections.Generic;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

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
        Assert.Single(vm.Lines);
        Assert.Contains("Applied Gaming", vm.Lines[0].Text);
    }

    [Fact]
    public void Appends_a_line_when_an_event_is_published()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "Disabled telemetry", "done"));
        Assert.Single(vm.Lines);
        Assert.Contains("Disabled telemetry", vm.Lines[0].Text);
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "x", "y"));
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Lines);
    }
}
