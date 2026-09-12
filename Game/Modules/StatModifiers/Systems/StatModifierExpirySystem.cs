using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.StatModifiers.Systems;

/// <summary>
/// Removes each StatModifierComponent on the exact frame it expires, at every processing tier,
/// and publishes StatModifierExpiredEvent for what it removed -- generic (any module can
/// subscribe), not just for AbilityScoresModule's benefit.
///
/// Scheduling is per entity, not per modifier: an entity's earliest deadline lives in its own
/// ExpiringStatModifierComponent (see that component for why), which is the only thing on the
/// timer wheel. This system keeps it up to date itself, from the StatModifierComponent pool's
/// change notification -- so a timed modifier granted by any path at all is scheduled, including
/// the action-effect path (StatModifierGrant) that writes the pool directly. A permanent modifier
/// (FrameDeadline.Never) is never scheduled, so an entity holding only permanent modifiers -- most
/// of the population -- costs nothing, which is what the old tiered walk needed a separate
/// membership marker to approximate.
///
/// A firing sweeps that one entity's chain in two passes, not one, because RemoveFirst compacts
/// the *whole pool's* dense array (swap-last-into-slot), which would corrupt an in-progress
/// GetNextDenseIndex chain walk if a removal happened mid-walk: pass 1 collects the Targets that
/// are due (removal itself doesn't report what it removed), pass 2 removes them. Events are
/// published after the removals are safely done, and the next deadline is computed after that --
/// a subscriber that grants a fresh modifier in response is then already accounted for, rather
/// than being clobbered by a deadline computed before it existed.
/// </summary>
public sealed class StatModifierExpirySystem : ISystem
{
    /// <summary>Every frame; the wheel only touches entities with a modifier actually due.</summary>
    public byte StripeCount => 1;

    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<ExpiringStatModifierComponent> _expiries;
    private readonly EventBus _eventBus;
    private readonly PackedTimerWheel<ExpiringStatModifierComponent> _wheel;
    private readonly List<StatModifierTarget> _pendingExpirations = [];

    // Cached once instead of passing the method group every Update -- an instance method group
    // conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<ExpiringStatModifierComponent> _tick;

    public StatModifierExpirySystem(
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<ExpiringStatModifierComponent> expiries,
        EventBus eventBus)
    {
        _statModifiers = statModifiers;
        _expiries = expiries;
        _eventBus = eventBus;
        _tick = Tick;
        _wheel = new PackedTimerWheel<ExpiringStatModifierComponent>(expiries);

        // Modifiers already granted before this system existed (a blueprint-built entity, a test
        // that populates first) get the same treatment as one granted a moment later -- the
        // notification below only covers writes from here on.
        for (var denseIndex = 0; denseIndex < statModifiers.Count; denseIndex++)
        {
            OnModifierChanged(statModifiers.GetEntityIdByDenseIndex(denseIndex), denseIndex);
        }

        statModifiers.ComponentChanged += OnModifierChanged;
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _tick);

    /// <summary>Keeps the entity's expiry timer no later than this modifier's own deadline. The pool's merge policy keeps the earlier of the two (see StatModifiersModule), so this is safe to call for every write, and writing it at all is what puts the entity on the wheel.</summary>
    private void OnModifierChanged(int entityId, int denseIndex)
    {
        var deadline = _statModifiers.GetReadonlyByDenseIndex(denseIndex).ExpiresAtFrame;
        if (deadline == FrameDeadline.Never)
        {
            return;
        }

        _expiries.Merge(entityId, new ExpiringStatModifierComponent(deadline));
    }

    /// <summary>Returns whether the entity's expiry timer itself should be removed -- true once nothing expiring is left on it. See TimerFired's contract.</summary>
    private bool Tick(int entityId, ExpiringStatModifierComponent expiry, long now)
    {
        _pendingExpirations.Clear();

        for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref _statModifiers.GetReadonlyByDenseIndex(denseIndex);
            if (IsDue(modifier.ExpiresAtFrame, now))
            {
                _pendingExpirations.Add(modifier.Target);
            }
        }

        while (_statModifiers.RemoveFirst(entityId, now, static (ref readonly StatModifierComponent modifier, long frame) => IsDue(modifier.ExpiresAtFrame, frame)))
        {
        }

        foreach (var target in _pendingExpirations)
        {
            _eventBus.Publish(new StatModifierExpiredEvent(entityId, target));
        }

        var nextDeadline = EarliestDeadline(entityId);
        if (nextDeadline == FrameDeadline.Never)
        {
            return true;
        }

        // Re-armed to the real deadline even when this frame has already reached it -- a subscriber
        // above can grant a modifier expiring now or earlier. The wheel schedules any deadline
        // written from inside a firing, this one included, and sweeps a reached one on the next
        // frame (late, never lost), so there is nothing to round up to here and the component
        // carries the deadline it actually has.
        _expiries.TryUpdate(entityId, nextDeadline, static (ref ExpiringStatModifierComponent e, uint deadline) => e.NextTickFrame = deadline);
        return false;
    }

    private static bool IsDue(uint expiresAtFrame, long now) => expiresAtFrame != FrameDeadline.Never && FrameDeadline.IsReached(expiresAtFrame, now);

    /// <summary>The earliest deadline still pending on the entity, or FrameDeadline.Never when nothing of its remaining modifiers ever expires.</summary>
    private uint EarliestDeadline(int entityId)
    {
        var earliest = FrameDeadline.Never;
        for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
        {
            earliest = System.Math.Min(earliest, _statModifiers.GetReadonlyByDenseIndex(denseIndex).ExpiresAtFrame);
        }

        return earliest;
    }
}
