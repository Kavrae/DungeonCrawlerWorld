using Engine.ECS.Entities;
using Engine.ECS.Systems;

namespace Engine.Decisions;

/// <summary>An entity's kept pick: which behavior, until which frame, and who it is about.</summary>
/// <remarks>
/// A deadline, not a countdown: nothing visits it, and once the frame is reached it is simply stale, so a game keeps it
/// in a component it never removes and rewrites in place when a new one starts. BehaviorSessionId is session-local, never
/// persisted. Target is whatever the picked behavior's Act left in the context, held as a key because it outlives the
/// frame; the engine never resolves it.
/// </remarks>
public struct BehaviorCommitment(ushort behaviorSessionId, uint committedUntilFrame, EntityKey target)
{
    /// <summary>The committed behavior's session id (BehaviorCatalog); 0 for none.</summary>
    public ushort BehaviorSessionId { get; set; } = behaviorSessionId;

    public uint CommittedUntilFrame { get; set; } = committedUntilFrame;

    public EntityKey Target { get; set; } = target;

    /// <summary>Whether it still holds at frame now: it names a behavior and its deadline isn't reached.</summary>
    public readonly bool IsLiveAt(long now) => BehaviorSessionId != 0 && !FrameDeadline.IsReached(CommittedUntilFrame, now);
}
