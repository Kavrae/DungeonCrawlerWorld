namespace Game.Modules.NpcBehavior.Components;

/// <summary>Marks an entity that decides what to do (NpcDecisionSystem): which behavior set it decides with, and until when it idles.</summary>
/// <remarks>
/// Added when an entity is built from a blueprint whose behavior set isn't empty, and rewritten when a part applied to
/// it changes the set (NpcDecisionBuildStep) -- so the pool is the system's population, stationary creatures included.
/// BehaviorSetId is the session-local id of the blueprint the set is resolved from (NpcBehaviorSets), never persisted.
/// WaitUntilFrame is a deadline, not a countdown: the decision gate compares it against the frame and nothing advances it.
/// </remarks>
public struct NpcDecisionComponent(ushort behaviorSetId)
{
    public ushort BehaviorSetId { get; set; } = behaviorSetId;

    /// <summary>The frame it may decide again on after choosing to idle; reached (or 0) means it isn't waiting.</summary>
    public uint WaitUntilFrame { get; set; }
}
