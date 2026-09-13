using Engine.ECS.Components;

namespace Engine.ECS.Systems;

/// <summary>Deadline arithmetic expressed on the timer itself, so the periodic case can't be written the wrong way.</summary>
/// <remarks>
/// Re-arming a periodic timer has exactly one correct base -- the deadline that just fired, not the
/// frame it was handled on (see <see cref="FrameDeadline.Repeat"/>) -- and the two agree on every
/// firing that isn't late, so the wrong version passes its tests and only drifts the cadence in
/// play. Naming the period and nothing else removes the choice: a caller reaching for
/// <see cref="RepeatEvery{T}"/> never reaches for <c>now</c> at all, so there is no rule left to
/// remember. This is the only shape a periodic re-arm should take.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ScheduledTimerExtensions
{
    /// <summary>Re-arms the timer one period on from the deadline that just fired, holding a fixed cadence even when a firing ran late. A parked timer (<see cref="FrameDeadline.Never"/>) stays parked.</summary>
    /// <remarks>How a periodic timer re-arms itself from inside its own <see cref="TimerFired{T}"/> callback: call it on the <c>ref</c> component a pool updater hands you, and the write schedules the next firing on its own (see <see cref="IScheduledTimer"/>).</remarks>
    public static void RepeatEvery<T>(this ref T timer, int periodFrames) where T : struct, IScheduledTimer =>
        timer.NextTickFrame = FrameDeadline.Repeat(timer.NextTickFrame, periodFrames);
}
