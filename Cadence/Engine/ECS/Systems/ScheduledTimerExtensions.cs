using Engine.ECS.Components;

namespace Engine.ECS.Systems;

/// <summary>Deadline arithmetic expressed on the timer itself, so the periodic case can't be written the wrong way.</summary>
/// <remarks>
/// Re-arming a periodic timer has exactly one correct base -- the deadline that just fired, not the
/// frame it was handled on (see <see cref="FrameDeadline.NextPeriodAfter"/>) -- and the two agree on every
/// firing that isn't late, so the wrong version passes its tests and only drifts the cadence in
/// play. Naming the period and nothing else removes the choice: a caller reaching for
/// <see cref="AdvanceOnePeriod{T}"/> never reaches for <c>now</c> at all, so there is no rule left to
/// remember. This is the only shape a periodic re-arm should take.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ScheduledTimerExtensions
{
    /// <summary>Moves the timer's deadline one period past the deadline that just fired, scheduling exactly one more firing. A parked timer (<see cref="FrameDeadline.Never"/>) stays parked.</summary>
    /// <remarks>A periodic timer calls it again from each <see cref="TimerFired{T}"/> callback, on the <c>ref</c> a write scope or pool updater hands it; the write schedules the next firing on its own (see <see cref="IScheduledTimer"/>). Counting from the fired deadline rather than the handling frame holds a fixed cadence even when a firing ran late.</remarks>
    public static void AdvanceOnePeriod<T>(this ref T timer, int periodFrames) where T : struct, IScheduledTimer =>
        timer.NextTickFrame = FrameDeadline.NextPeriodAfter(timer.NextTickFrame, periodFrames);

    /// <summary>Moves the timer past every period owed up to <paramref name="now"/> without firing any of them -- see <see cref="FrameDeadline.SkipOwed"/>.</summary>
    public static void SkipOwedPeriods<T>(this ref T timer, int periodFrames, long now) where T : struct, IScheduledTimer =>
        timer.NextTickFrame = FrameDeadline.SkipOwed(timer.NextTickFrame, periodFrames, now);
}
