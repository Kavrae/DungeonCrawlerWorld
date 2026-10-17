namespace Engine.ECS.Components;

/// <summary>A component that fires at an absolute simulation frame, driven by a PackedTimerWheel or MultiTimerWheel.</summary>
/// <remarks>
/// Game code writes only <see cref="NextTickFrame"/> -- an absolute deadline (see
/// Engine.ECS.Systems.FrameDeadline), set when the timer is created (FrameDeadline.After(now,
/// duration)) and re-armed by the owning system's tick callback (RepeatEvery(period), which needs
/// no `now` at all -- see Engine.ECS.Systems.ScheduledTimerExtensions). Writing it through any
/// normal pool write (Add, Merge, TryUpdate, ...) is enough: the pool's ComponentChanged observer
/// schedules it on the wheel. Nothing else has to be remembered. A deadline of FrameDeadline.Never
/// parks the timer: it is never scheduled.
///
/// <see cref="TimerWheelMark"/> is the wheel's own bookkeeping, not game state: implement it as a
/// plain auto-property and never write it. It records which deadline the wheel already holds an
/// entry for (stored +1, so the default 0 means "none"), which is what lets a write that leaves
/// the deadline unchanged -- a stack top-off, a merge -- skip scheduling a duplicate.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IScheduledTimer
{
    /// <summary>The simulation frame this timer next fires on.</summary>
    uint NextTickFrame { get; set; }

    /// <summary>Wheel bookkeeping -- never written by game code. See the interface remarks.</summary>
    uint TimerWheelMark { get; set; }
}

/// <summary>An <see cref="IScheduledTimer"/> in a MultiComponentPool, where an entity can carry several -- <see cref="TimerKey"/> tells its instances apart.</summary>
/// <remarks>
/// A MultiComponentPool dense index isn't a stable identity (removal elsewhere moves instances), so
/// a wheel entry names the instance by (entityId, TimerKey) instead. The key must be unique among
/// one entity's instances and fixed for the instance's lifetime -- a StatusEffectType, a body part
/// id.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IKeyedScheduledTimer : IScheduledTimer
{
    int TimerKey { get; }
}
