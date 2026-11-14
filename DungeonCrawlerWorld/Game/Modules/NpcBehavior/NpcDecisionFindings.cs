using Engine.ECS.Entities;
using Engine.Math;

namespace Game.Modules.NpcBehavior;

/// <summary>What a behavior's Evaluate found for its Act, carried from the choose pass to the act pass as a plain value.</summary>
/// <remarks>
/// Keys, ids and stack instance ids only -- never a dense index or a span, which an earlier act in the pass may have
/// invalidated -- so Act never searches again, and what it acts on is looked up afresh. Each behavior writes only its
/// own fields; the winner's are the ones read.
/// </remarks>
public struct NpcDecisionFindings
{
    /// <summary>The self-heal SelfHeal chose: an action's Guid, or Guid.Empty when it chose an item.</summary>
    public Guid SelfHealActionId { get; set; }

    /// <summary>The stack of the self-heal SelfHeal chose, when it chose an item.</summary>
    public uint SelfHealStackInstanceId { get; set; }

    /// <summary>The corpse or item pile LootNearby chose.</summary>
    public EntityKey LootTarget { get; set; }

    /// <summary>Whether looking for loot found only corpses reserved for someone else: the decision then waits to retry corpses (NpcLooting.RetryLater), whatever it chose.</summary>
    public bool RetryCorpsesLater { get; set; }

    /// <summary>The hostile Flee runs from: the nearest it saw when it started, then its commitment's target.</summary>
    public EntityKey FleeFrom { get; set; }

    /// <summary>The tile beside it Flee steps to: the one farthest from FleeFrom, and farther than where it stands.</summary>
    public Vector3Int FleeStep { get; set; }
}
