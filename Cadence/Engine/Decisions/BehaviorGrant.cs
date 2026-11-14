using Engine.Tags;

namespace Engine.Decisions;

/// <summary>One behavior given to a kind of entity: the highest rank it can apply at, its weight, a number it interprets, how long a pick of it is kept, and what scales it.</summary>
/// <remarks>Immutable and shared by every entity that holds it. A later grant of the same behavior in a composition replaces an earlier one; Rank 0 takes it away.</remarks>
/// <param name="Rank">The rank ceiling: Evaluate may return it or lower, never higher.</param>
/// <param name="Parameter">One number the behavior interprets (a health threshold, a baseline); the engine never reads it.</param>
/// <param name="CommitFrames">How long a fresh pick of it is kept against rivals of its rank and below; 0 for never.</param>
public sealed record BehaviorGrant<TContext>(
    Guid BehaviorId,
    byte Rank,
    float Weight = 1f,
    float Parameter = 0f,
    ushort CommitFrames = 0,
    IReadOnlyList<IBehaviorConsideration<TContext>>? Considerations = null)
    where TContext : IDecisionContext, allows ref struct;

/// <summary>Multiplies the weight of every behavior carrying Tag (parent-aware), or of the one behavior BehaviorId names.</summary>
/// <remarks>Names exactly one of the two; a set refuses one naming both or neither. One aimed at a behavior a set doesn't hold does nothing.</remarks>
public sealed record BehaviorModifier(float WeightMultiplier, GameplayTag? Tag = null, Guid? BehaviorId = null);
