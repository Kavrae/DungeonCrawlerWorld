namespace Engine.ECS.Systems;

/// <summary>Arithmetic for absolute-frame deadlines: "ready at frame N" instead of "N frames left".</summary>
/// <remarks>
/// A deadline is a uint simulation frame (2^32 frames is ~2.2 years at 60fps). Nothing advances it;
/// readers compare it against the current frame (EngineTime.FrameCount, or SimulationClock). This
/// is how a countdown that merely *reaches* 0 -- a lock, a cooldown -- costs nothing per frame: there
/// is no system walking it down.
///
/// Semantics, in one place so every caller agrees: a deadline set during frame F for D frames is
/// <see cref="After"/>(F, D) = F + D. It is still running on frames F .. F+D-1 and reached from
/// frame F+D on -- D frames of wait, the same count the old "D frames remaining" meant. D = 0 is
/// reached immediately. A default (0) deadline is always reached, which is what a freshly created
/// component means by "not waiting on anything".
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class FrameDeadline
{
    /// <summary>A deadline that is never reached. Reserved for parked/permanent timers; <see cref="After"/> never produces it.</summary>
    public const uint Never = uint.MaxValue;

    /// <summary>The deadline <paramref name="frames"/> frames after <paramref name="now"/>, saturating just below <see cref="Never"/>.</summary>
    public static uint After(long now, int frames)
    {
        var deadline = now + System.Math.Max(0, frames);
        return deadline >= Never ? Never - 1 : (uint)System.Math.Max(0, deadline);
    }

    /// <summary>The first deadline of a periodic timer: about <paramref name="frames"/> after <paramref name="now"/>, spread by <paramref name="entityId"/> across one period centred on that delay.</summary>
    /// <remarks>
    /// <see cref="Repeat"/> counts every later deadline from the one before, so periodic timers started on
    /// the same frame come due on the same frame for as long as they run. Without a spread, a
    /// population spawned in one frame and standing in lava ticks all at once, once a second. 
    /// The spread is a fixed function of the entity id, so a seeded run stays deterministic;
    /// the multiplicative hash keeps entities created next to each other, with consecutive ids, from
    /// landing on consecutive frames.
    /// </remarks>
    public static uint AfterStaggered(long now, int frames, int entityId)
    {
        if (frames <= 1)
        {
            return After(now, frames);
        }

        var spread = (int)(unchecked((uint)entityId * 2654435761u) % (uint)frames);
        return After(now, frames - frames / 2 + spread);
    }

    /// <summary>The next deadline of a fixed-rate repeat: <paramref name="frames"/> after the deadline that just fired, not after the frame it was handled on.</summary>
    /// <remarks>
    /// How a periodic timer re-arms itself from inside its own firing, in preference to
    /// <see cref="After"/>(now, frames). The two agree whenever a timer fires on its exact frame,
    /// which is every firing while the simulation advances a frame at a time. They part company
    /// once one fires late (a gap in the frames drained, or a deadline scheduled in the past):
    /// After counts the period from the late frame, so the whole schedule slides with it and the
    /// firings the gap covered are lost, while this keeps the original cadence and lets the wheel
    /// catch the owed firings up, one per frame. A parked timer (<see cref="Never"/>) stays parked.
    /// </remarks>
    public static uint Repeat(uint previousDeadline, int frames) =>
        previousDeadline == Never ? Never : After(previousDeadline, frames);

    /// <summary>The first deadline on <paramref name="deadline"/>'s cadence that is later than <paramref name="now"/> -- every period owed up to now skipped, none of them fired. A deadline already later than now, or parked, is returned unchanged.</summary>
    /// <remarks>For a timer whose owed firings must not happen at all, such as an exposure that should not accrue while its entity was frozen. Keeps the cadence, so entities that shared a phase before stay spread the way they were.</remarks>
    public static uint SkipOwed(uint deadline, int frames, long now)
    {
        if (deadline == Never || deadline > now || frames <= 0)
        {
            return deadline;
        }

        var owedPeriods = (now - deadline) / frames + 1;
        return After(deadline, (int)System.Math.Min(int.MaxValue, owedPeriods * frames));
    }

    /// <summary>True from the deadline's frame on.</summary>
    public static bool IsReached(uint deadline, long now) => now >= deadline;

    /// <summary>Frames until the deadline is reached; 0 once it has been.</summary>
    public static int Remaining(uint deadline, long now) =>
        deadline == Never ? int.MaxValue : (int)System.Math.Clamp(deadline - now, 0, int.MaxValue);
}
