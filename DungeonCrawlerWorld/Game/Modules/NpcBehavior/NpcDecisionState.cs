using Engine.Decisions;
using Game.Blueprints;

namespace Game.Modules.NpcBehavior;

/// <summary>A build's NPC decisions as a whole: the behaviors modules register, the sets blueprints resolve to, the counters the decision system keeps and the one entity it traces for inspection.</summary>
/// <remarks>Created with GameModuleContext, so every member is there from construction: modules register behaviors in Configure, and NpcBehaviorModule freezes the catalog and resolves every set in RegisterBehavior.</remarks>
public sealed class NpcDecisionState
{
    public NpcDecisionState(BlueprintRegistry definitions) => Sets = new NpcBehaviorSets(definitions, Behaviors);

    /// <summary>Every behavior an NPC can be granted; a mod registers its own, or replaces a built-in by its Guid, in Configure.</summary>
    public BehaviorCatalog<NpcDecisionContext> Behaviors { get; } = new();

    public NpcBehaviorSets Sets { get; }

    /// <summary>Decisions made so far, for the NpcBehavior/Decisions gauge.</summary>
    public long Decisions { get; internal set; }

    /// <summary>Commitments started so far, for the NpcBehavior/CommitmentsStarted gauge.</summary>
    public long CommitmentsStarted { get; internal set; }

    /// <summary>The one entity the decision system traces; -1 for none.</summary>
    public int InspectedEntityId { get; set; } = -1;

    /// <summary>The inspected entity's last decision, layer by layer.</summary>
    public DecisionTrace InspectedTrace { get; } = new();

    /// <summary>The frame of the inspected entity's last decision; -1 before it has made one since it was inspected.</summary>
    public long InspectedDecisionFrame { get; internal set; } = -1;

    /// <summary>The name of the behavior its last decision chose; null when nothing applied.</summary>
    public string? InspectedChoice { get; internal set; }

    /// <summary>Inspects entityId from now on, forgetting what was traced for another.</summary>
    public void Inspect(int entityId)
    {
        if (entityId == InspectedEntityId)
        {
            return;
        }

        InspectedEntityId = entityId;
        InspectedDecisionFrame = -1;
        InspectedChoice = null;
    }

    /// <summary>Stops inspecting entityId if it is the one inspected: its id is about to mean another entity.</summary>
    public void Release(int entityId)
    {
        if (entityId == InspectedEntityId)
        {
            Inspect(-1);
        }
    }
}
