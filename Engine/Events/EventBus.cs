namespace Engine.Events;

/// <summary>
/// Lightweight typed pub/sub, letting modules react to each other (e.g. a future
/// death/respawn module reacting to health reaching zero) without referencing each other
/// directly. Supports two dispatch modes, selected by the event type itself (see
/// <see cref="IBufferedEvent"/>): immediate, where Publish invokes subscribers synchronously
/// in-line (the default, unchanged from before buffering existed), and buffered, where
/// Publish enqueues and delivery waits for an explicit DispatchBuffered&lt;T&gt;() call.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, Delegate> _subscribers = [];
    private readonly Dictionary<Type, object> _bufferedQueues = [];

    public void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _subscribers[typeof(T)] = _subscribers.TryGetValue(typeof(T), out var existing)
            ? (Action<T>)existing + handler
            : handler;
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_subscribers.TryGetValue(typeof(T), out var existing))
        {
            return;
        }

        var updated = (Action<T>)existing - handler;
        if (updated is null)
        {
            _subscribers.Remove(typeof(T));
        }
        else
        {
            _subscribers[typeof(T)] = updated;
        }
    }

    /// <summary>
    /// Dispatches immediately if T is not an <see cref="IBufferedEvent"/> (unchanged
    /// behavior); otherwise enqueues eventData for the next DispatchBuffered&lt;T&gt;() call
    /// instead of invoking subscribers here.
    /// </summary>
    public void Publish<T>(T eventData)
    {
        if (eventData is IBufferedEvent)
        {
            GetOrCreateQueue<T>().Enqueue(eventData);
            return;
        }

        if (_subscribers.TryGetValue(typeof(T), out var existing))
        {
            ((Action<T>)existing).Invoke(eventData);
        }
    }

    /// <summary>
    /// Invokes every current subscriber against everything queued for T since the last call,
    /// then clears the queue. A no-op if nothing was queued (including if T was never
    /// published at all). Only meaningful for T implementing <see cref="IBufferedEvent"/> --
    /// nothing is ever queued for an immediate-dispatch type, so this is always a no-op there.
    /// </summary>
    public void DispatchBuffered<T>()
    {
        if (!_bufferedQueues.TryGetValue(typeof(T), out var queueObject))
        {
            return;
        }

        var queue = (Queue<T>)queueObject;
        if (queue.Count == 0)
        {
            return;
        }

        var hasSubscribers = _subscribers.TryGetValue(typeof(T), out var existing);

        while (queue.Count > 0)
        {
            var eventData = queue.Dequeue();
            if (hasSubscribers)
            {
                ((Action<T>)existing!).Invoke(eventData);
            }
        }
    }

    private Queue<T> GetOrCreateQueue<T>()
    {
        if (_bufferedQueues.TryGetValue(typeof(T), out var existing))
        {
            return (Queue<T>)existing;
        }

        var queue = new Queue<T>();
        _bufferedQueues[typeof(T)] = queue;
        return queue;
    }
}