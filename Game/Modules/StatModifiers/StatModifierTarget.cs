namespace Game.Modules.StatModifiers;

/// <summary>
/// Which stat a StatModifierComponent affects. Extensible the same way StatusEffectType is --
/// new stats add new members here as something needs to modify them.
/// </summary>
public enum StatModifierTarget
{
    OutgoingDamage,
    MaximumHealth,
    HealthRegen,

    /// <summary>Damage an entity receives, consumed at HealthDamage.Apply -- the single chokepoint for every damage source (abilities, Burning, Poison, contact hazards) -- so a reduction here applies uniformly regardless of what dealt the damage.</summary>
    IncomingDamage,
}
