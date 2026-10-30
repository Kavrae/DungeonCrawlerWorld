namespace Game.Modules.StatModifiers;

/// <summary>One target's accumulated additive and multiplicative modifier sums, before a base value is folded in.</summary>
public readonly record struct StatModifierSums(float Additive, float Multiplicative)
{
    /// <summary>The effective value of baseValue under these sums (StatModifierMath.CalculateTotal).</summary>
    public float ApplyTo(float baseValue) => StatModifierMath.CalculateTotal(baseValue, Additive, Multiplicative);
}
