using Engine.Tags;

namespace Engine.Decisions;

/// <summary>One thing an entity can decide to do: how much it applies now, and what doing it writes.</summary>
/// <remarks>
/// Stateless and shared by every entity granted it: everything particular to an entity is in the context, everything
/// particular to a kind of entity is in its grant. Evaluate only reads -- it writes nothing outside the context and draws
/// no random number -- so every entity can be evaluated before any acts. Act runs on the winner only, and is the one
/// place a behavior writes or rolls.
/// </remarks>
public interface IBehavior<TContext>
    where TContext : IDecisionContext, allows ref struct
{
    Guid Id { get; }

    string Name { get; }

    /// <summary>What kind of behavior it is: what a modifier or a runtime multiplier targets.</summary>
    GameplayTagSet Tags { get; }

    /// <summary>The rank it applies at -- at most grant.Rank, 0 when it doesn't apply -- and its weight from the situation alone, at least 0.</summary>
    BehaviorEvaluation Evaluate(ref TContext context, BehaviorGrant<TContext> grant);

    /// <summary>Writes what the entity does.</summary>
    void Act(ref TContext context, BehaviorGrant<TContext> grant);
}

/// <summary>How a behavior applies right now: its rank (0 when it doesn't) and its weight from the situation alone.</summary>
public readonly record struct BehaviorEvaluation(byte Rank, float Weight)
{
    public static BehaviorEvaluation DoesNotApply => default;
}

/// <summary>A situational factor a grant multiplies its behavior's weight by.</summary>
/// <remarks>Read-only, as Evaluate is. It is asked only once its behavior already has weight.</remarks>
public interface IBehaviorConsideration<TContext>
    where TContext : IDecisionContext, allows ref struct
{
    /// <summary>A multiplier of at least 0; 0 vetoes the behavior.</summary>
    float Evaluate(ref TContext context);
}
