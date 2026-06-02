namespace Optimizer.WinUI.Services.Events;

/// <summary>
/// In-process pub/sub bus for domain events raised by the Optimizer app.
///
/// Threading: <see cref="Publish"/> is safe to call from any thread.
/// Subscribers are invoked synchronously on the publishing thread.
/// Subscribers that need to update UI must marshal to the UI thread themselves
/// (e.g. via DispatcherQueue.TryEnqueue).
///
/// Cloud forwarding: each published event is forwarded to the server on a
/// background queue (fire-and-forget). Failures are swallowed silently.
/// </summary>
public interface IEventBus
{
    /// <summary>Publish an event to all matching subscribers.</summary>
    void Publish(OptimizerEvent evt);

    /// <summary>Subscribe to all event types. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(Action<OptimizerEvent> handler);

    /// <summary>Subscribe to events of a specific type. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(OptimizerEventType type, Action<OptimizerEvent> handler);

    /// <summary>Ring buffer of the last N events (for late subscribers / UI history).</summary>
    IReadOnlyList<OptimizerEvent> RecentEvents { get; }
}
