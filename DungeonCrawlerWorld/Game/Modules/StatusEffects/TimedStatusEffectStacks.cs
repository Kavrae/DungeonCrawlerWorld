using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>The rules every timed status effect (ITimedStatusEffectTimer) shares: adding stacks, counting its ticks down, and how long it has left.</summary>
/// <remarks>
/// Stacks add up to the effect's cap. A duration never adds: the effect lasts the longer of what it had and what was
/// just granted, whether or not any stack fits. A new effect's first tick is one interval after it lands, staggered by
/// entity; adding to a running one leaves its tick where it is. A grant lasting no ticks starts nothing.
/// </remarks>
public static class TimedStatusEffectStacks
{
    /// <summary>Adds up to count stacks to entityId's effect, starting it if it isn't running, and returns how many landed.</summary>
    /// <remarks>Immunity is the caller's to check first. A running effect takes the duration even when no stack lands (count 0, or at the cap).</remarks>
    /// <param name="durationTicks">The duration this grant gives, already scaled.</param>
    public static StatusEffectStacksApplied Apply<TTimer>(PackedComponentPool<TTimer> timers, int entityId, int count, int maxStacks, ushort durationTicks, ActionSource source, long now, int tickIntervalFrames)
        where TTimer : struct, ITimedStatusEffectTimer<TTimer>
    {
        var hasTimer = timers.TryGetReadonly(entityId, out var existingTimer);
        var stacksLanded = Math.Max(0, Math.Min(count, maxStacks - (hasTimer ? existingTimer.StackCount : 0)));

        if (!hasTimer)
        {
            if (stacksLanded == 0 || durationTicks == 0)
            {
                return StatusEffectStacksApplied.Landed(0);
            }

            timers.Add(entityId, TTimer.Create(FrameDeadline.AfterStaggered(now, tickIntervalFrames, entityId), (byte)stacksLanded, durationTicks, source));
            return StatusEffectStacksApplied.Landed(stacksLanded);
        }

        if (stacksLanded > 0 || durationTicks > existingTimer.RemainingDurationTicks)
        {
            timers.TryUpdate(entityId, new AddStacksUpdate<TTimer>((byte)stacksLanded, durationTicks));
        }

        return StatusEffectStacksApplied.Landed(stacksLanded);
    }

    /// <summary>Whether a timer that just fired has no ticks left, so it ends without doing anything.</summary>
    public static bool IsSpent<TTimer>(in TTimer timer) where TTimer : struct, ITimedStatusEffectTimer<TTimer> =>
        timer.RemainingDurationTicks == 0;

    /// <summary>Counts off the tick the timer just fired for and schedules the next; returns whether that was its last, so the wheel removes it.</summary>
    public static bool CountDownAfterTick<TTimer>(PackedComponentPool<TTimer> timers, int entityId, in TTimer timer, int tickIntervalFrames)
        where TTimer : struct, ITimedStatusEffectTimer<TTimer>
    {
        var remainingDurationTicks = (ushort)(timer.RemainingDurationTicks - 1);
        if (remainingDurationTicks == 0)
        {
            return true;
        }

        using var tickedTimer = timers.TryWrite(entityId);
        if (tickedTimer.Found)
        {
            tickedTimer.Value.RemainingDurationTicks = remainingDurationTicks;
            tickedTimer.Value.AdvanceOnePeriod(tickIntervalFrames);
        }

        return false;
    }

    /// <summary>Frames until the effect ends as of now: the running tick, then one interval per tick left after it.</summary>
    public static int RemainingFrames<TTimer>(in TTimer timer, long now, int tickIntervalFrames) where TTimer : struct, ITimedStatusEffectTimer<TTimer> =>
        FrameDeadline.Remaining(timer.NextTickFrame, now) + (timer.RemainingDurationTicks - 1) * tickIntervalFrames;

    private readonly struct AddStacksUpdate<TTimer>(byte addedStacks, ushort durationTicks) : IComponentUpdater<TTimer>
        where TTimer : struct, ITimedStatusEffectTimer<TTimer>
    {
        public void Update(ref TTimer timer)
        {
            timer.StackCount += addedStacks;
            timer.RemainingDurationTicks = Math.Max(timer.RemainingDurationTicks, durationTicks);
        }
    }
}
