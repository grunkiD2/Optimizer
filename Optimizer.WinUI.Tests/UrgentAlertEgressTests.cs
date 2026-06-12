using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class UrgentAlertEgressTests
{
    [Fact]
    public async Task Unconfigured_egress_returns_false_without_pushing()
    {
        var pushed = 0;
        var egress = new UrgentAlertEgress("", (t, m, ct) => { pushed++; return Task.FromResult(true); });
        Assert.False(egress.IsConfigured);
        Assert.False(await egress.PushUrgentAsync("title", "msg"));
        Assert.Equal(0, pushed);
    }

    [Fact]
    public async Task Push_invokes_the_notify_channel_with_title_and_message()
    {
        var calls = new List<(string Title, string Msg)>();
        var egress = new UrgentAlertEgress(@"C:\fake\state", (t, m, ct) => { calls.Add((t, m)); return Task.FromResult(true); });
        Assert.True(egress.IsConfigured);
        Assert.True(await egress.PushUrgentAsync("Drive failure predicted", "Back up now."));
        var call = Assert.Single(calls);
        Assert.Equal("Drive failure predicted", call.Title);
        Assert.Equal("Back up now.", call.Msg);
    }

    [Fact]
    public async Task Same_title_is_cooldown_deduplicated()
    {
        var pushed = 0;
        var egress = new UrgentAlertEgress(@"C:\fake\state", (t, m, ct) => { pushed++; return Task.FromResult(true); });
        Assert.True(await egress.PushUrgentAsync("Drive failure predicted", "msg 1"));
        Assert.False(await egress.PushUrgentAsync("Drive failure predicted", "msg 2"));   // hourly evaluator re-fires
        Assert.True(await egress.PushUrgentAsync("CPU thermal warning", "different title passes"));
        Assert.Equal(2, pushed);
    }

    [Fact]
    public async Task Pusher_failure_is_swallowed_and_reported_as_false()
    {
        var egress = new UrgentAlertEgress(@"C:\fake\state",
            (t, m, ct) => throw new InvalidOperationException("network down"));
        Assert.False(await egress.PushUrgentAsync("title", "msg"));   // must not throw
    }
}
