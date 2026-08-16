using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.Events;

/// <summary> Lightweight typed pub/sub, letting modules react to each other without direct reference </summary>
/// <remarks> Supports two dispatch modes, selected by the event type : 
/// Immediate, where Publish invokes subscribers synchronously in-line (the default)
/// Buffered, where Publish enqueues and delivery waits for an explicit DispatchBuffered&lt;T&gt;() call.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EventBus
{
    private readonly Dictionary<Type, Delegate> _subscribers = [];
    private readonly Dictionary<Type, object> _bufferedQueues = [];

    /// <summary>
    /// Cached event type name per T, built once on first profiled Publish rather than
    /// re-interpolated every call -- unlike a per-handler cost breakdown (reverted: on a
    /// hyper-frequent event like EntityMovedEvent, splitting one timed region into several
    /// multiplied Stopwatch/Record calls enough to become a measurable fraction of the reported
    /// cost itself), this cache only depends on T, not on the current handler set, so
    /// Subscribe/Unsubscribe don't need to invalidate it.
    /// </summary>
    private readonly Dictionary<Type, string> _eventTypeNames = [];

    /// <summary>
    /// Opt-in dispatch-cost tracking, recorded under FrameCostCategory.Update, group "EventBus",
    /// item = the event's type name -- see FrameBudgetTracker's own doc comment. Immediate
    /// (non-buffered) dispatch runs subscribers synchronously in-line with whatever called
    /// Publish, so a system that publishes mid-Update (e.g. MovementSystem publishing
    /// EntityMovedEvent) has every subscriber's cost nested inside that system's own
    /// SystemManager.Profiler timing -- this records dispatch cost separately, so the two can be
    /// told apart when tracking down a gameplay demo's actual frame cost. Null (the default)
    /// skips the Stopwatch calls entirely.
    /// </summary>
    public IFrameCostRecorder? Profiler { get; set; }

    /// <summary>Subscribes to events of type T.</summary>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="handler">The handler to invoke when an event of type T is published.</param>
    public void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _subscribers[typeof(T)] = _subscribers.TryGetValue(typeof(T), out var existing)
            ? (Action<T>)existing + handler
            : handler;
    }

    /// <summary>Subscribes handler for exactly one T matching condition, then unsubscribes itself.</summary>
    /// <remarks>Omitting condition means "fire on the first T published, unconditionally." handler runs after the internal wrapper has already unsubscribed, so handler is free to Publish/Subscribe more of T itself without re-triggering this same one-shot.</remarks>
    public void SubscribeOnce<T>(Action<T> handler, Func<T, bool>? condition = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        Action<T>? wrapper = null;
        wrapper = eventData =>
        {
            if (condition is not null && !condition(eventData))
            {
                return;
            }

            Unsubscribe(wrapper!);
            handler(eventData);
        };

        Subscribe(wrapper);
    }

    /// <summary>Unsubscribes the specified handler from events of type T.</summary>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="handler">The handler to unsubscribe.</param>
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

    /// <summary>Publishes an event of type T.</summary>
    /// <remarks>Events of type <see cref="IBufferedEvent"/> are queued for later dispatch, while other events are dispatched immediately.</remarks>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="eventData">The event data to publish.</param>
    public void Publish<T>(T eventData)
    {
        if (eventData is IBufferedEvent)
        {
            GetOrCreateQueue<T>().Enqueue(eventData);
            return;
        }

        if (!_subscribers.TryGetValue(typeof(T), out var existing))
        {
            return;
        }

        if (Profiler is { } profiler)
        {
            if (!_eventTypeNames.TryGetValue(typeof(T), out var eventTypeName))
            {
                eventTypeName = typeof(T).Name;
                _eventTypeNames[typeof(T)] = eventTypeName;
            }

            var start = Stopwatch.GetTimestamp();
            ((Action<T>)existing).Invoke(eventData);
            profiler.Record(FrameCostCategory.Update, "EventBus", eventTypeName, Stopwatch.GetElapsedTime(start));
        }
        else
        {
            ((Action<T>)existing).Invoke(eventData);
        }
    }

    /// <summary>Dispatches all buffered events of type T.</summary>
    /// <remarks>This allows systems to control when their own buffered events are processed to avoid data corruption and take advantage of bulk processing.</remarks>
    /// <typeparam name="T">The type of the event.</typeparam>
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