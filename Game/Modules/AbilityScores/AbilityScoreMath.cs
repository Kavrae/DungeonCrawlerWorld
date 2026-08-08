using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Clamping and effective-value computation for ability scores. Total is precomputed by
/// AbilityScoreEffects at grant/change time (see its own doc comment for why this is eager
/// rather than the lazy-on-read convention StatModifierMath uses for every other stat) --
/// this class supplies the math both that eager write path and unit tests need, it doesn't
/// itself cache anything.
/// </summary>
public static class AbilityScoreMath
{
    public const short MinimumBaseValue = 1;
    public const short MaximumBaseValue = 300;

    // Precomputed once rather than divided out on every Lerp call -- Lerp runs on hot paths
    // (per-visit regen ticks, per-target potion cooldown resets), and a multiply by a cached
    // reciprocal is cheaper than a float divide repeated across every one of those calls.
    private static readonly float InverseBaseValueRange = 1f / (MaximumBaseValue - MinimumBaseValue);

    public static short ClampBaseValue(short baseValue) => MathUtility.ClampShort(baseValue, MinimumBaseValue, MaximumBaseValue);

    /// <summary>
    /// Linear ramp from atMin (at ability score total MinimumBaseValue) to atMax (at
    /// MaximumBaseValue) -- abilityScoreTotal is clamped into that range first via
    /// ClampBaseValue. Shared by every "output scales smoothly across an ability score's full
    /// range" formula (AbilityScoreRegenMath, PotionCooldownEffects.ComputeDurationFrames, ...)
    /// so they don't each duplicate the same normalize-then-lerp arithmetic. atMin may be
    /// greater than atMax -- callers whose output should shrink as the score rises (e.g. a
    /// cooldown) just pass their endpoints in that order.
    /// </summary>
    public static float Lerp(short abilityScoreTotal, float atMin, float atMax)
    {
        var clampedTotal = ClampBaseValue(abilityScoreTotal);
        var normalized = (clampedTotal - MinimumBaseValue) * InverseBaseValueRange;
        return atMin + normalized * (atMax - atMin);
    }

    /// <summary>Which StatModifierTarget a given AbilityScoreType's modifiers are filed under -- the two enums are deliberately kept 1:1 (see StatModifierTarget's own comment).</summary>
    public static StatModifierTarget ToStatModifierTarget(AbilityScoreType type) => type switch
    {
        AbilityScoreType.Strength => StatModifierTarget.Strength,
        AbilityScoreType.Intelligence => StatModifierTarget.Intelligence,
        AbilityScoreType.Constitution => StatModifierTarget.Constitution,
        AbilityScoreType.Dexterity => StatModifierTarget.Dexterity,
        AbilityScoreType.Charisma => StatModifierTarget.Charisma,
        AbilityScoreType.Luck => StatModifierTarget.Luck,
        AbilityScoreType.Wisdom => StatModifierTarget.Wisdom,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Reverse of ToStatModifierTarget -- null for any StatModifierTarget that isn't an ability score (e.g. MaximumHealth), the signal AbilityScoreEffects uses to no-op a StatModifierComponent change it doesn't need to react to.</summary>
    public static AbilityScoreType? FromStatModifierTarget(StatModifierTarget target) => target switch
    {
        StatModifierTarget.Strength => AbilityScoreType.Strength,
        StatModifierTarget.Intelligence => AbilityScoreType.Intelligence,
        StatModifierTarget.Constitution => AbilityScoreType.Constitution,
        StatModifierTarget.Dexterity => AbilityScoreType.Dexterity,
        StatModifierTarget.Charisma => AbilityScoreType.Charisma,
        StatModifierTarget.Luck => AbilityScoreType.Luck,
        StatModifierTarget.Wisdom => AbilityScoreType.Wisdom,
        _ => null,
    };

    /// <summary>
    /// pool may be null -- same reasoning as StatModifierMath.GetEffectiveValue: callers
    /// building a module set without StatModifiersModule still work, just with no possible
    /// active modifiers. Clamps in the int domain before casting to short (see MathUtility.ClampByte
    /// for the same reasoning) -- a float far outside short's range would otherwise produce an
    /// unspecified value on cast rather than a clean clamp.
    /// </summary>
    public static short ComputeTotal(MultiComponentPool<StatModifierComponent>? statModifiers, int entityId, AbilityScoreType type, short baseValue)
    {
        var effectiveValue = StatModifierMath.GetEffectiveValue(statModifiers, entityId, ToStatModifierTarget(type), baseValue);
        return (short)MathUtility.ClampInt((int)effectiveValue, 0, short.MaxValue);
    }
}
