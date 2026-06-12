using System.Collections.Concurrent;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Events;

/// <summary>
/// Thread-safe in-process event bus.
///
/// Events are delivered synchronously to all registered handlers on the
/// calling thread. Each handler is wrapped in try/catch so a throwing
/// subscriber never breaks others or the publisher.
/// </summary>
public sealed class EventBus : IEventBus
{
    private const int RingBufferCapacity = 100;

    // Subscriber registry: each entry holds a unique ID and handler delegate
    private readonly ConcurrentDictionary<Guid, Action<OptimizerEvent>> _subscribers = new();

    // Ring buffer for RecentEvents
    private readonly object _ringLock = new();
    private readonly Queue<OptimizerEvent> _ringBuffer = new();

    // ── IEventBus ─────────────────────────────────────────────────────────────

    public IReadOnlyList<OptimizerEvent> RecentEvents
    {
        get
        {
            lock (_ringLock)
                return _ringBuffer.ToList().AsReadOnly();
        }
    }

    public void Publish(OptimizerEvent evt)
    {
        // Add to ring buffer
        lock (_ringLock)
        {
            _ringBuffer.Enqueue(evt);
            while (_ringBuffer.Count > RingBufferCapacity)
                _ringBuffer.Dequeue();
        }

        // Deliver to subscribers (each in its own try/catch)
        foreach (var (_, handler) in _subscribers)
        {
            try
            {
                handler(evt);
            }
            catch (Exception ex)
            {
                EngineLog.Error($"[EventBus] Subscriber threw on event {evt.Type}", ex);
            }
        }
    }

    public IDisposable Subscribe(Action<OptimizerEvent> handler)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = handler;
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    public IDisposable Subscribe(OptimizerEventType type, Action<OptimizerEvent> handler)
        => Subscribe(evt => { if (evt.Type == type) handler(evt); });

    // ── Subscription handle ───────────────────────────────────────────────────

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
