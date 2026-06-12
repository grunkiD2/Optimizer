using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Optimizer.WinUI.Services.Events;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Unit tests for EventBus pub/sub, ring buffer, type filtering, and safety guarantees.
/// </summary>
public class EventBusTests
{
    private readonly EventBus _bus = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    private static OptimizerEvent MakeEvent(OptimizerEventType type = OptimizerEventType.OptimizationApplied)
        => OptimizerEvent.Create(type, "Title", "Detail");

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_GeneralSubscriber_Receives()
    {
        OptimizerEvent? received = null;
        _bus.Subscribe(evt => received = evt);

        var published = MakeEvent();
        _bus.Publish(published);

        Assert.NotNull(received);
        Assert.Equal(published.Type, received!.Type);
    }

    [Fact]
    public void Publish_TypeFilteredSubscriber_ReceivesMatchingType()
    {
        var received = new List<OptimizerEvent>();
        _bus.Subscribe(OptimizerEventType.PluginInstalled, evt => received.Add(evt));

        _bus.Publish(MakeEvent(OptimizerEventType.OptimizationApplied));
        _bus.Publish(MakeEvent(OptimizerEventType.PluginInstalled));
        _bus.Publish(MakeEvent(OptimizerEventType.AnomalyDetected));

        Assert.Single(received);
        Assert.Equal(OptimizerEventType.PluginInstalled, received[0].Type);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        int count = 0;
        var handle = _bus.Subscribe(_ => count++);

        _bus.Publish(MakeEvent());
        Assert.Equal(1, count);

        handle.Dispose();
        _bus.Publish(MakeEvent());
        Assert.Equal(1, count); // still 1 — no extra delivery
    }

    [Fact]
    public void MultipleSubscribers_AllReceive()
    {
        int countA = 0, countB = 0, countC = 0;
        _bus.Subscribe(_ => countA++);
        _bus.Subscribe(_ => countB++);
        _bus.Subscribe(_ => countC++);

        _bus.Publish(MakeEvent());

        Assert.Equal(1, countA);
        Assert.Equal(1, countB);
        Assert.Equal(1, countC);
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotBreakOthers()
    {
        int countOk = 0;
        _bus.Subscribe(_ => throw new InvalidOperationException("test throw"));
        _bus.Subscribe(_ => countOk++);

        // Should not throw
        _bus.Publish(MakeEvent());

        Assert.Equal(1, countOk);
    }

    [Fact]
    public void RecentEvents_RingBuffer_CapsAtCapacity()
    {
        // Publish 110 events — ring buffer should cap at 100
        for (int i = 0; i < 110; i++)
            _bus.Publish(MakeEvent());

        Assert.Equal(100, _bus.RecentEvents.Count);
    }

    [Fact]
    public void RecentEvents_ContainsPublishedEvents()
    {
        _bus.Publish(MakeEvent(OptimizerEventType.ProfileApplied));
        _bus.Publish(MakeEvent(OptimizerEventType.AnomalyDetected));

        var recent = _bus.RecentEvents;
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, e => e.Type == OptimizerEventType.ProfileApplied);
        Assert.Contains(recent, e => e.Type == OptimizerEventType.AnomalyDetected);
    }

    [Fact]
    public void Create_FactoryMethod_SetsTimestampAndType()
    {
        var before = DateTime.UtcNow;
        var evt = OptimizerEvent.Create(OptimizerEventType.ThresholdCrossed, "T", "D");
        var after = DateTime.UtcNow;

        Assert.Equal(OptimizerEventType.ThresholdCrossed, evt.Type);
        Assert.InRange(evt.TimestampUtc, before, after);
        Assert.Equal("T", evt.Title);
        Assert.Equal("D", evt.Detail);
    }

    [Fact]
    public void ThreadSafety_PublishFromMultipleThreads_NoCrash()
    {
        // Each subscriber increments a counter; 10 threads × 50 publishes = 500
        int received = 0;
        _bus.Subscribe(_ => Interlocked.Increment(ref received));

        var threads = Enumerable.Range(0, 10)
            .Select(_ => new Thread(() =>
            {
                for (int i = 0; i < 50; i++)
                    _bus.Publish(MakeEvent());
            }))
            .ToList();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(5));

        Assert.Equal(500, received);
    }

    [Fact]
    public void Create_WithData_StoresData()
    {
        var data = new Dictionary<string, string> { ["key"] = "value" };
        var evt = OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "T", "D", data);

        Assert.NotNull(evt.Data);
        Assert.Equal("value", evt.Data!["key"]);
    }
}
