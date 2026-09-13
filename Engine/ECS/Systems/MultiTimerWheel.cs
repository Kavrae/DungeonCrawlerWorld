using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary>PackedTimerWheel's counterpart for a MultiComponentPool, where an entity can carry several timers of one type, told apart by TimerKey.</summary>
/// <remarks>
/// Same contract as <see cref="PackedTimerWheel{T}"/> -- see its remarks. The differences are all
/// about instance identity: an entry names its timer by (entityId, TimerKey) because dense indices
/// move on removal; validation walks the entity's chain to find that instance; and removal takes
/// out that instance only. The pool's ComponentChanged fires per instance, so a second timer added
/// to an entity that already has one is scheduled like any other -- the case EntityAdded can't see.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MultiTimerWheel<T> where T : struct, IKeyedScheduledTimer
{
    private readonly MultiComponentPool<T> _pool;
    private readonly TimerWheel _wheel = new();
    private readonly List<TimerEntry> _due = [];
    private readonly List<TimerEntry> _pendingRemovals = [];

    /// <summary>Instances written while <see cref="_draining"/>, whose scheduling is decided once the firing pass is over. Named by (entity, key), since removals move dense indices.</summary>
    private readonly List<(int EntityId, int Key)> _pendingSchedules = [];

    /// <summary>True for the firing pass only -- see PackedTimerWheel's remarks.</summary>
    private bool _draining;

    /// <inheritdoc cref="PackedTimerWheel{T}(PackedComponentPool{T})"/>
    public MultiTimerWheel(MultiComponentPool<T> pool)
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

    /// <inheritdoc cref="PackedTimerWheel{T}.PendingCount"/>
    public int PendingCount => _wheel.PendingCount;

    /// <inheritdoc cref="PackedTimerWheel{T}.Tick"/>
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
                var denseIndex = FindInstance(entry.EntityId, entry.Key);
                if (denseIndex < 0 || !ScheduledTimerMark.IsScheduledFor(_pool.GetReadonlyByDenseIndex(denseIndex), entry.Deadline))
                {
                    continue;
                }

                // Released before the callback runs, not after -- see PackedTimerWheel.Tick.
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
            _draining = false;
            ApplyDeferredSchedules();
        }

        foreach (var entry in _pendingRemovals)
        {
            // Only the instance that actually fired -- see PackedTimerWheel.Tick's own flush. The
            // identity check matters more here: Add puts a new instance at the *head* of the
            // entity's chain, so a same-key instance created during this drain is precisely the one
            // FindInstance would hand back, and removing it would drop a live timer while leaving
            // the spent one in place.
            var denseIndex = FindInstance(entry.EntityId, entry.Key);
            if (denseIndex >= 0 && _pool.GetReadonlyByDenseIndex(denseIndex).NextTickFrame == entry.Deadline)
            {
                _pool.RemoveByDenseIndex(denseIndex);
            }
        }
    }

    /// <inheritdoc cref="PackedTimerWheel{T}.ApplyDeferredSchedules"/>
    private void ApplyDeferredSchedules()
    {
        foreach (var (entityId, key) in _pendingSchedules)
        {
            var denseIndex = FindInstance(entityId, key);
            if (denseIndex >= 0)
            {
                Schedule(entityId, denseIndex);
            }
        }

        _pendingSchedules.Clear();
    }

    /// <inheritdoc cref="PackedTimerWheel{T}.Schedule"/>
    private void Schedule(int entityId, int denseIndex)
    {
        ref var timer = ref _pool.GetByDenseIndex(denseIndex);
        if (ScheduledTimerMark.TryClaim(ref timer, out var deadline))
        {
            _wheel.Schedule(entityId, timer.TimerKey, deadline);
        }
    }

    /// <summary>The dense index of entityId's instance carrying <paramref name="key"/>, or -1. Valid only until the next removal from the pool.</summary>
    private int FindInstance(int entityId, int key)
    {
        for (var denseIndex = _pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _pool.GetNextDenseIndex(denseIndex))
        {
            if (_pool.GetReadonlyByDenseIndex(denseIndex).TimerKey == key)
            {
                return denseIndex;
            }
        }

        return -1;
    }

    /// <summary>ComponentChanged handler -- see PackedTimerWheel.Observe.</summary>
    private void Observe(int entityId, int denseIndex)
    {
        if (!_draining)
        {
            Schedule(entityId, denseIndex);
            return;
        }

        ref readonly var timer = ref _pool.GetReadonlyByDenseIndex(denseIndex);
        if (timer.NextTickFrame != FrameDeadline.Never)
        {
            _pendingSchedules.Add((entityId, timer.TimerKey));
        }
    }
}
