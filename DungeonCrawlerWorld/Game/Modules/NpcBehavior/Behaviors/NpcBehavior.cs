using Engine.Decisions;
using Engine.Tags;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>A built-in NPC behavior: its identity and tags, with Evaluate and Act left to it.</summary>
/// <remarks>Stateless and shared by every entity granted it, as IBehavior requires; a mod's behavior implements IBehavior directly or derives from this.</remarks>
public abstract class NpcBehavior(Guid id, string name, GameplayTagSet tags) : IBehavior<NpcDecisionContext>
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public GameplayTagSet Tags { get; } = tags;

    public abstract BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant);

    public abstract void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant);

    /// <summary>The grant's rank at weight 1: what a behavior that applies, with no weight of its own, returns.</summary>
    protected static BehaviorEvaluation Applies(NpcBehaviorGrant grant) => new(grant.Rank, 1f);
}

/// <summary>The Guids of the built-in behaviors, which grants name them by and a mod replaces them by.</summary>
public static class BuiltInBehaviorIds
{
    public static readonly Guid SelfHeal = new("5e1f0a00-4c0d-4b1e-9a55-000000000001");
    public static readonly Guid EngageMelee = new("5e1f0a00-4c0d-4b1e-9a55-000000000002");
    public static readonly Guid RangedAttack = new("5e1f0a00-4c0d-4b1e-9a55-000000000003");
    public static readonly Guid LootNearby = new("5e1f0a00-4c0d-4b1e-9a55-000000000004");
    public static readonly Guid WanderStep = new("5e1f0a00-4c0d-4b1e-9a55-000000000005");
    public static readonly Guid Idle = new("5e1f0a00-4c0d-4b1e-9a55-000000000006");
    public static readonly Guid Flee = new("5e1f0a00-4c0d-4b1e-9a55-000000000007");
}
