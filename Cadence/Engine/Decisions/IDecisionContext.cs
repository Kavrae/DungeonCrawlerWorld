using Engine.ECS.Entities;
using Engine.Tags;

namespace Engine.Decisions;

/// <summary>What the arbiter needs from any decision context; a game's context implements it, and may be a ref struct.</summary>
/// <remarks>
/// The context is the game's: it carries whatever its behaviors read and write. The arbiter reads only the frame, a
/// random number when it has to choose between candidates, the runtime weight of a behavior's tags, and the target a
/// commitment is held to.
/// </remarks>
public interface IDecisionContext
{
    /// <summary>The frame the decision is made on, which a commitment's deadline is measured from.</summary>
    long Now { get; }

    /// <summary>A number uniform in [0, 1) from the caller's seeded sequence; the arbiter owns no randomness.</summary>
    float NextUnitRandom();

    /// <summary>What a behavior carrying behaviorTags is weighted by at this moment; 1 when nothing applies.</summary>
    float RuntimeWeightMultiplier(GameplayTagSet behaviorTags);

    /// <summary>Who the behavior being evaluated or acted on is committed to: the live commitment's target while it continues, None for a fresh pick.</summary>
    /// <remarks>The arbiter sets it before the committed behavior is asked to continue and clears it before a fresh pick; whatever a fresh pick's Act leaves in it is the new commitment's target.</remarks>
    EntityKey CommitmentTarget { get; set; }
}
