using Engine.ECS.Components;

namespace Engine.ECS.Systems;

/// <summary>The one place <see cref="IScheduledTimer.TimerWheelMark"/>'s encoding lives: which deadline a wheel already holds an entry for, stored +1 so the struct default (0) reads as "none".</summary>
/// <remarks>
/// Both wheels (<see cref="PackedTimerWheel{T}"/>, <see cref="MultiTimerWheel{T}"/>) validate,
/// claim and release marks identically -- only the instance lookup differs between them -- so the
/// encoding and its invariants are spelled out here once rather than in four places.
///
/// A deadline never reaches <see cref="FrameDeadline.Never"/> here (a parked timer is never
/// scheduled) and <see cref="FrameDeadline.After"/> saturates below it, so <see cref="For"/> can't
/// wrap onto <see cref="None"/>.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class ScheduledTimerMark
{
    /// <summary>No entry is scheduled for this timer -- the struct default, and what a wheel writes back the moment a deadline is spent.</summary>
    public const uint None = 0;

    /// <summary>The mark a timer carries while a wheel holds an entry for <paramref name="deadline"/>.</summary>
    public static uint For(uint deadline) => deadline + 1;

    /// <summary>True if the timer is still set for, and scheduled for, exactly <paramref name="deadline"/> -- the lazy-cancellation check every firing runs.</summary>
    public static bool IsScheduledFor<T>(in T timer, uint deadline) where T : struct, IScheduledTimer =>
        timer.NextTickFrame == deadline && timer.TimerWheelMark == For(deadline);

    /// <summary>Claims the timer's current deadline for the wheel, or returns false when there is nothing to schedule: a parked timer, or one already marked for that same deadline (a stack top-off, a merge that left the deadline alone).</summary>
    /// <remarks>The mark write is raw and deliberately unobserved -- no version bump, no re-entrant ComponentChanged. It is wheel bookkeeping, not game state.</remarks>
    public static bool TryClaim<T>(ref T timer, out uint deadline) where T : struct, IScheduledTimer
    {
        deadline = timer.NextTickFrame;
        if (deadline == FrameDeadline.Never || timer.TimerWheelMark == For(deadline))
        {
            return false;
        }

        timer.TimerWheelMark = For(deadline);
        return true;
    }

    /// <summary>Releases the claim: this deadline is spent, so the next write of any deadline -- the same one included -- schedules the timer anew.</summary>
    /// <remarks>Raw and unobserved, same as <see cref="TryClaim{T}"/>.</remarks>
    public static void Release<T>(ref T timer) where T : struct, IScheduledTimer => timer.TimerWheelMark = None;
}
