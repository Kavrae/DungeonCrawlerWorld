namespace Game.Modules.AbilityScores.Components;

/// <summary>
/// One ability score on one entity -- MultiComponentPool, same shape as RaceComponent/ClassComponent:
/// every entity that has ability scores gets exactly one instance per AbilityScoreType (7 total),
/// not "N sources" the way StatModifierComponent stacks. The flat/multiplicative modifiers
/// themselves live in MultiComponentPool&lt;StatModifierComponent&gt; (filterable by the matching
/// StatModifierTarget) -- this struct only holds the untouched base and the precomputed result.
/// </summary>
public struct AbilityScoreComponent(AbilityScoreType type, short baseValue, short total)
{
    public AbilityScoreType Type { get; } = type;

    /// <summary>Clamped [1, 300] -- see AbilityScoreMath.ClampBaseValue.</summary>
    public short BaseValue { get; set; } = baseValue;

    /// <summary>base combined with whichever StatModifierComponents currently target this score, clamped [0, short.MaxValue] -- precomputed by AbilityScoreEffects at grant/change time, not read-time (see AbilityScoreMath).</summary>
    public short Total { get; set; } = total;
}
