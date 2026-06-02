using System.Collections.Concurrent;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services.Cloud;

namespace Optimizer.WinUI.Services.Events;

/// <summary>
/// Thread-safe in-process event bus with optional cloud forwarding.
///
/// Events are delivered synchronously to all registered handlers on the
/// calling thread. Each handler is wrapped in try/catch so a throwing
/// subscriber never breaks others or the publisher.
///
/// Cloud forwarding is queued to a background <see cref="Channel{T}"/> so
/// <see cref="Publish"/> never blocks on network I/O.
/// </summary>
public sealed class EventBus : IEventBus, IDisposable
{
    private const int RingBufferCapacity = 100;
    private const int CloudQueueCapacity = 500;

    private readonly IOptimizerCloudClient _cloud;

    // Subscriber registry: each entry holds a unique ID and handler delegate
    private readonly ConcurrentDictionary<Guid, Action<OptimizerEvent>> _subscribers = new();

    // Ring buffer for RecentEvents
    private readonly object _ringLock = new();
    private readonly Queue<OptimizerEvent> _ringBuffer = new();

    // Background cloud-forwarding queue
    private readonly System.Threading.Channels.Channel<OptimizerEvent> _cloudQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _forwardTask;

    public EventBus(IOptimizerCloudClient cloud)
    {
        _cloud = cloud;

        _cloudQueue = System.Threading.Channels.Channel.CreateBounded<OptimizerEvent>(
            new System.Threading.Channels.BoundedChannelOptions(CloudQueueCapacity)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        _forwardTask = Task.Run(ForwardLoopAsync);
    }

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

        // Queue for cloud forwarding (non-blocking)
        if (_cloud.IsAuthenticated)
            _cloudQueue.Writer.TryWrite(evt);
    }

    public IDisposable Subscribe(Action<OptimizerEvent> handler)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = handler;
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    public IDisposable Subscribe(OptimizerEventType type, Action<OptimizerEvent> handler)
        => Subscribe(evt => { if (evt.Type == type) handler(evt); });

    // ── Cloud forwarding loop ─────────────────────────────────────────────────

    private async Task ForwardLoopAsync()
    {
        try
        {
            await foreach (var evt in _cloudQueue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await _cloud.ForwardEventAsync(
                        evt.Type.ToString(),
                        evt.Title,
                        evt.Detail,
                        evt.Data);
                }
                catch
                {
                    // Best-effort: swallow failures silently
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _cloudQueue.Writer.TryComplete();
        try { _forwardTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

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
