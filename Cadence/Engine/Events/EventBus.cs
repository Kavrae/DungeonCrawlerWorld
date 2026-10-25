using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.Events;

/// <summary> Lightweight typed pub/sub, letting modules react to each other without direct reference </summary>
/// <remarks> Supports two dispatch modes, selected by the event type :
/// Immediate, where Publish invokes subscribers synchronously in-line (the default)
/// Buffered, where Publish enqueues and delivery waits for an explicit DispatchBuffered&lt;T&gt;() call.
/// Each event type has a process-wide slot (EventTypeSlot), so finding a type's subscribers is an array read rather than a dictionary lookup.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EventBus
{
    private static int _nextEventTypeSlot;

    private Delegate?[] _subscribersBySlot = [];
    private object?[] _bufferedQueuesBySlot = [];
    private string?[] _eventTypeNamesBySlot = [];

    /// <summary>Subscribes to events of type T.</summary>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="handler">The handler to invoke when an event of type T is published.</param>
    public void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var slot = EventTypeSlot<T>.Index;
        EnsureSlot(ref _subscribersBySlot, slot);
        _subscribersBySlot[slot] = _subscribersBySlot[slot] is Action<T> existing ? existing + handler : handler;
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

        if (SubscribersOf<T>() is not { } existing)
        {
            return;
        }

        _subscribersBySlot[EventTypeSlot<T>.Index] = existing - handler;
    }

    /// <summary>Publishes an event of type T.</summary>
    /// <remarks>
    /// Events of type <see cref="IBufferedEvent"/> are queued for later dispatch, while other events are dispatched immediately.
    ///
    /// While EngineHooks.FrameCosts has a listener, immediate dispatch cost is recorded under
    /// FrameCostCategory.Update, group "EventBus", item = the event's type name. Immediate dispatch runs
    /// subscribers in-line with whatever called Publish, so a system that publishes mid-Update (a movement
    /// system announcing each move) has every subscriber's cost nested inside that system's
    /// own SystemManager entry -- this records it separately, so the two can be told apart.
    ///
    /// That is a branch on the listener rather than an EngineHooks.FrameCost wrapper, because Publish runs
    /// thousands of times a frame: a wrapper would enter a try/finally on every call, even with nothing listening.
    /// </remarks>
    /// <typeparam name="T">The type of the event.</typeparam>
    /// <param name="eventData">The event data to publish.</param>
    public void Publish<T>(T eventData)
    {
        if (eventData is IBufferedEvent)
        {
            GetOrCreateQueue<T>().Enqueue(eventData);
            return;
        }

        if (SubscribersOf<T>() is not { } subscribers)
        {
            return;
        }

        if (EngineHooks.FrameCosts.Listener is { } frameCostRecorder)
        {
            var eventTypeName = EventTypeNameOf<T>();
            frameCostRecorder.Begin(FrameCostCategory.Update, "EventBus", eventTypeName);
            var start = Stopwatch.GetTimestamp();
            try
            {
                subscribers.Invoke(eventData);
            }
            finally
            {
                frameCostRecorder.Record(FrameCostCategory.Update, "EventBus", eventTypeName, Stopwatch.GetElapsedTime(start));
            }
        }
        else
        {
            subscribers.Invoke(eventData);
        }
    }

    /// <summary>Dispatches all buffered events of type T.</summary>
    /// <remarks>
    /// This allows systems to control when their own buffered events are processed to avoid data corruption and take advantage of bulk processing.
    /// An event of type T published while this runs (a death that causes another death) is dispatched by this same call, after the ones already queued,
    /// so a handler that always publishes another T never returns. Subscribers are read once, when the call starts.
    /// </remarks>
    /// <typeparam name="T">The type of the event.</typeparam>
    public void DispatchBuffered<T>()
    {
        var slot = EventTypeSlot<T>.Index;
        if ((uint)slot >= (uint)_bufferedQueuesBySlot.Length || _bufferedQueuesBySlot[slot] is not Queue<T> { Count: > 0 } queue)
        {
            return;
        }

        var subscribers = SubscribersOf<T>();

        while (queue.Count > 0)
        {
            var eventData = queue.Dequeue();
            subscribers?.Invoke(eventData);
        }
    }

    private Action<T>? SubscribersOf<T>()
    {
        var slot = EventTypeSlot<T>.Index;
        var subscribersBySlot = _subscribersBySlot;
        return (uint)slot < (uint)subscribersBySlot.Length ? (Action<T>?)subscribersBySlot[slot] : null;
    }

    private Queue<T> GetOrCreateQueue<T>()
    {
        var slot = EventTypeSlot<T>.Index;
        EnsureSlot(ref _bufferedQueuesBySlot, slot);
        return (Queue<T>)(_bufferedQueuesBySlot[slot] ??= new Queue<T>());
    }

    private string EventTypeNameOf<T>()
    {
        var slot = EventTypeSlot<T>.Index;
        EnsureSlot(ref _eventTypeNamesBySlot, slot);
        return _eventTypeNamesBySlot[slot] ??= typeof(T).Name;
    }

    private static void EnsureSlot<TEntry>(ref TEntry?[] entriesBySlot, int slot)
    {
        if (slot < entriesBySlot.Length)
        {
            return;
        }

        Array.Resize(ref entriesBySlot, System.Math.Max(slot + 1, System.Math.Max(16, entriesBySlot.Length * 2)));
    }

    /// <summary>The slot every EventBus keeps T's subscribers, queue and name in: assigned once per process, the first time any bus meets T.</summary>
    private static class EventTypeSlot<T>
    {
        public static readonly int Index = Interlocked.Increment(ref _nextEventTypeSlot) - 1;
    }
}
