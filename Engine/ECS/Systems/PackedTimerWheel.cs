using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary>Called when a timer's deadline arrives. Return true to remove the component (removal is deferred until every due timer this frame has been handled).</summary>
/// <remarks>
/// To fire again, write a new NextTickFrame through any normal pool write -- the wheel picks it up
/// on its own, and the deadline may be anything, including the one that just fired or one already
/// behind <paramref name="now"/> (it fires on the next frame: late, never lost). A periodic timer
/// re-arms with <see cref="ScheduledTimerExtensions.RepeatEvery{T}"/> on the ref a pool updater
/// hands it -- naming only the period, so the cadence stays exact after a late firing and there is
/// no <c>now</c> to get wrong. Returning false without re-arming leaves the timer resting: it won't
/// fire again until something writes a new deadline.
///
/// Re-arming *and* returning true is contradictory, and the re-arm wins -- the component stays,
/// carrying its new deadline. A live deadline is never thrown away on the strength of a removal
/// decided before it existed (see PackedTimerWheel.Tick's own flush).
/// </remarks>
public delegate bool TimerFired<T>(int entityId, T timer, long now) where T : struct;

/// <summary>Drives every <see cref="IScheduledTimer"/> in one PackedComponentPool from a <see cref="TimerWheel"/>: scheduling is automatic, firing is O(timers due).</summary>
/// <remarks>
/// Replaces the visit-every-entity-and-decrement loop timers used to run on. The owning system
/// calls <see cref="Tick"/> once per frame; nothing else in the game needs to know the wheel exists.
///
/// Scheduling is automatic (PLAN-timer-wheel.md, design 2b): the wheel observes the pool's
/// ComponentChanged, so any write that changes a timer's NextTickFrame -- creating it, re-arming
/// it, moving it earlier or later, from any code path -- schedules it. A write that leaves the
/// deadline alone is ignored (see <see cref="ScheduledTimerMark"/>). Components already in the pool
/// when the wheel is built are scheduled on construction.
///
/// Firing validates each due entry against the live component (lazy cancellation): it fires only
/// if the entity still has the timer and it is still set for, and scheduled for, exactly that
/// deadline. Anything removed, re-armed or recycled since is dropped silently.
///
/// A Tick is two phases, for the same reason an immutable array is rebuilt rather than edited
/// under its own readers: scheduling state is what the firing pass validates against, so the
/// firing pass never mutates it. Writes made from inside a callback are collected, and the
/// schedule-or-not decision for each is taken afterwards, against the component as it finally
/// settled. Marks are therefore a stable snapshot for the whole pass, and no callback can make a
/// spent entry look live again.
///
/// Exactly one wheel may drive a pool, since the scheduling mark lives on the component rather
/// than in the wheel; the pool enforces that (see TimerWheelClaim) instead of trusting callers.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class PackedTimerWheel<T> where T : struct, IScheduledTimer
{
    private readonly PackedComponentPool<T> _pool;
    private readonly TimerWheel _wheel = new();
    private readonly List<TimerEntry> _due = [];
    private readonly List<TimerEntry> _pendingRemovals = [];

    /// <summary>Entities written while <see cref="_draining"/>, whose scheduling is decided once the firing pass is over.</summary>
    private readonly List<int> _pendingSchedules = [];

    /// <summary>True for the firing pass only. See the class remarks: while set, Observe defers instead of deciding.</summary>
    private bool _draining;

    /// <param name="pool">The timer pool to drive. The wheel claims it (only one wheel per pool) and subscribes to its ComponentChanged for its own lifetime.</param>
    public PackedTimerWheel(PackedComponentPool<T> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pool.ClaimForTimerWheel();

        _pool = pool;

        for (var denseIndex = 0; denseIndex < pool.Count; denseIndex++)
        {
            Observe(pool.GetEntityIdByDenseIndex(denseIndex), denseIndex);
        }

        pool.ComponentChanged += Observe;
    }

    /// <summary>Entries waiting in the wheel, stale ones included. Diagnostics and tests.</summary>
    public int PendingCount => _wheel.PendingCount;

    /// <summary>Fires every timer due up to and including <paramref name="now"/>, then removes the ones whose callback asked to be removed.</summary>
    public void Tick(long now, TimerFired<T> onFired)
    {
        ArgumentNullException.ThrowIfNull(onFired);

        _due.Clear();
        _pendingRemovals.Clear();
        _wheel.DrainDue(now, _due);
        _draining = true;

        try
        {
            foreach (var entry in _due)
            {
                var denseIndex = _pool.GetDenseIndex(entry.EntityId);
                if (denseIndex < 0 || !ScheduledTimerMark.IsScheduledFor(_pool.GetReadonlyByDenseIndex(denseIndex), entry.Deadline))
                {
                    continue;
                }

                // Release the claim BEFORE the callback, not after. The wheel cannot tell a callback
                // that wrote the same deadline back from one that left the deadline alone, and
                // releasing first makes both land correctly: an untouched timer rests (nothing
                // re-claims it), while any write at all -- this deadline included -- is collected
                // below and scheduled afresh. Releasing afterwards instead clobbered the second
                // case, leaving a live deadline with nothing scheduled for it, and every consumer
                // re-arming from a computed deadline had to dodge that itself.
                ref var timer = ref _pool.GetByDenseIndex(denseIndex);
                ScheduledTimerMark.Release(ref timer);

                if (onFired(entry.EntityId, _pool.GetReadonlyByDenseIndex(denseIndex), now))
                {
                    _pendingRemovals.Add(entry);
                }
            }
        }
        finally
        {
            // In a finally, so a throwing callback still leaves everything written before it on the
            // wheel: a lost schedule doesn't heal by itself -- the timer would rest with a live
            // deadline nothing fires -- whereas a lost removal simply fires and asks again.
            _draining = false;
            ApplyDeferredSchedules();
        }

        foreach (var entry in _pendingRemovals)
        {
            // Remove the timer that fired, not whatever now sits under its entity id: a callback
            // later in this same drain could have re-armed or re-created it, and deleting that
            // would silently lose a live timer. Still carrying the spent deadline is what
            // identifies it as the one that fired -- anything else has been written since, and a
            // write is the only way back onto the wheel.
            var denseIndex = _pool.GetDenseIndex(entry.EntityId);
            if (denseIndex >= 0 && _pool.GetReadonlyByDenseIndex(denseIndex).NextTickFrame == entry.Deadline)
            {
                _pool.Remove(entry.EntityId);
            }
        }
    }

    /// <summary>Takes the schedule-or-not decision for everything written during the firing pass, against the component as it finally settled.</summary>
    private void ApplyDeferredSchedules()
    {
        foreach (var entityId in _pendingSchedules)
        {
            var denseIndex = _pool.GetDenseIndex(entityId);
            if (denseIndex >= 0)
            {
                Schedule(entityId, denseIndex);
            }
        }

        _pendingSchedules.Clear();
    }

    /// <summary>Claims the component's current deadline and puts an entry on the wheel, unless it already holds one for that same deadline.</summary>
    private void Schedule(int entityId, int denseIndex)
    {
        ref var timer = ref _pool.GetByDenseIndex(denseIndex);
        if (ScheduledTimerMark.TryClaim(ref timer, out var deadline))
        {
            _wheel.Schedule(entityId, 0, deadline);
        }
    }

    /// <summary>ComponentChanged handler: schedules the timer if its deadline differs from the one already scheduled -- or defers that decision if a firing pass is in flight.</summary>
    private void Observe(int entityId, int denseIndex)
    {
        if (!_draining)
        {
            Schedule(entityId, denseIndex);
            return;
        }

        // Deferred rather than decided. Whether this write needs an entry depends on a mark the
        // firing pass is still releasing, and claiming one now would make a spent duplicate entry
        // for the same deadline validate again later in this very pass. A parked timer needs
        // nothing either way, so it is the one case worth settling here.
        if (_pool.GetReadonlyByDenseIndex(denseIndex).NextTickFrame != FrameDeadline.Never)
        {
            _pendingSchedules.Add(entityId);
        }
    }
}
