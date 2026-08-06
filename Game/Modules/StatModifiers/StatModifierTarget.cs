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

    // AbilityScoreType's 7 members, mirrored 1:1 -- lets equipment/class/buffs grant a
    // StatModifierComponent targeting an ability score via the existing StatModifierEffects.Apply,
    // with no new grant API. See AbilityScoreEffects for the write path that keeps
    // AbilityScoreComponent.Total in sync when one of these is granted or expires.
    Strength,
    Intelligence,
    Constitution,
    Dexterity,
    Charisma,
    Luck,
    Wisdom,
}
