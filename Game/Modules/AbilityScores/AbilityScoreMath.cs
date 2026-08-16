using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.AbilityScores;

/// <summary>Provides mathematical operations for handling ability scores.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class AbilityScoreMath
{
    /// <summary>The minimum allowed base value for an ability score.</summary>
    public const ushort MinimumBaseValue = 1;

    /// <summary>The maximum allowed base value for an ability score.</summary>
    public const ushort MaximumBaseValue = 300;

    // Precomputed once rather than divided out on every Lerp call -- Lerp runs on hot paths
    // (per-visit regen ticks, per-target potion cooldown resets), and a multiply by a cached
    // reciprocal is cheaper than a float divide repeated across every one of those calls.
    private static readonly float InverseBaseValueRange = 1f / (MaximumBaseValue - MinimumBaseValue);

    /// <summary>Clamps a base value for an ability score between the minimum and maximum allowed values.</summary>
    /// <param name="baseValue">The base value to clamp.</param>
    /// <returns>The clamped base value.</returns>
    public static ushort ClampBaseValue(ushort baseValue) => MathUtility.ClampUShort(baseValue, MinimumBaseValue, MaximumBaseValue);

    /// <summary>Linearly interpolates between two values based on an ability score total.</summary>
    /// <remarks>Values are normalized between 1 and 300.</remarks>
    /// <param name="abilityScoreTotal">The ability score total to interpolate against.</param>
    /// <param name="atMin">The value at the minimum base value.</param>
    /// <param name="atMax">The value at the maximum base value.</param>
    /// <returns>The interpolated value.</returns>
    public static float Lerp(ushort abilityScoreTotal, float atMin, float atMax)
    {
        var clampedTotal = ClampBaseValue(abilityScoreTotal);
        var normalized = (clampedTotal - MinimumBaseValue) * InverseBaseValueRange;
        return MathUtility.Lerp(normalized, atMin, atMax);
    }

    /// <summary>Converts an ability score type to its corresponding stat modifier target.</summary>
    /// <param name="type">The ability score type to convert.</param>
    /// <returns>The corresponding stat modifier target.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the ability score type is not recognized.</exception>
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

    /// <summary>Converts a stat modifier target to its corresponding ability score type.</summary>
    /// <param name="target">The stat modifier target to convert.</param>
    /// <returns>The corresponding ability score type, or null if not recognized.</returns>
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

    /// <summary>Computes the total value for an ability score based on its base value and active stat modifiers.</summary>
    /// <param name="statModifiers">The pool of active stat modifiers.</param>
    /// <param name="entityId">The ID of the entity for which to compute the total.</param>
    /// <param name="type">The ability score type.</param>
    /// <param name="baseValue">The base value for the ability score.</param>
    /// <returns>The computed total value.</returns>
    public static ushort ComputeTotal(MultiComponentPool<StatModifierComponent>? statModifiers, int entityId, AbilityScoreType type, ushort baseValue)
    {
        var effectiveValue = StatModifierMath.GetEffectiveValue(statModifiers, entityId, ToStatModifierTarget(type), baseValue);
        return MathUtility.ClampUShort(effectiveValue, 0, ushort.MaxValue);
    }
}
