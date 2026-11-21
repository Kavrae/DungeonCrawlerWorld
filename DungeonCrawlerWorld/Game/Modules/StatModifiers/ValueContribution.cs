using Engine.Tags;
using Game.Modules.AbilityScores;
using Game.World;

namespace Game.Modules.StatModifiers;

/// <summary>What changed a value on its way from its base to what it is when used.</summary>
public enum ValueContributionKind : byte
{
    /// <summary>A stat modifier on the entity using it (its Source names what gave it).</summary>
    Modifier,

    /// <summary>An ability score's total, added for an activation tagged with it (AbilityScoreTagBonus).</summary>
    AbilityScore,

    /// <summary>An aura's strength where it lands, multiplying the amounts an aura's effects carry (EffectContext.Magnitude).</summary>
    Strength,

    /// <summary>A scroll's Intelligence multiplier on its range, area and durations (ScrollScalingEffects).</summary>
    ScrollIntelligence,
}

/// <summary>One step between a value's base and its value as used.</summary>
/// <remarks>
/// Additive adds Amount. Multiplicative multiplies by 1 + Amount, as a modifier's magnitude does (StatModifierMath:
/// multiplicative modifiers add up before multiplying, so several are listed in turn but act as their sum). A
/// multiplier that isn't a modifier (a strength, a scroll's) is stored the same way: Amount is the multiplier minus 1.
/// </remarks>
/// <param name="Source">What gave a Modifier; null for every other kind.</param>
/// <param name="AbilityScore">Which score an AbilityScore contribution is; null for every other kind.</param>
/// <param name="ConditionTag">The tag a Modifier applies under, or None.</param>
public readonly record struct ValueContribution(
    ValueContributionKind Kind,
    StatModifierOperation Operation,
    float Amount,
    ActionSource? Source = null,
    AbilityScoreType? AbilityScore = null,
    GameplayTag ConditionTag = default)
{
    /// <summary>A multiplier that isn't a modifier, as the Multiplicative contribution it is.</summary>
    public static ValueContribution Multiplier(ValueContributionKind kind, float multiplier) =>
        new(kind, StatModifierOperation.Multiplicative, multiplier - 1f);
}
