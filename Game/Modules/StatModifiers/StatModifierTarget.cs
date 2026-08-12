namespace Game.Modules.StatModifiers;

/// <summary>
/// Which stat a StatModifierComponent affects. Extensible the same way StatusEffectType is --
/// new stats add new members here as something needs to modify them.
/// </summary>
public enum StatModifierTarget
{
    OutgoingDamage,
    MaximumHealth,

    /// <summary>Layers on top of HealthRegenSystem's live-computed (Constitution-derived) base regen amount -- see AbilityScoreRegenMath. Not a stored base value of its own; there's nothing left to modify in place, StatModifierMath.GetEffectiveValue is applied to the freshly-computed amount each visit.</summary>
    HealthRegen,

    MaximumMana,

    /// <summary>Mirrors HealthRegen -- layers on top of ManaRegenSystem's live-computed (Intelligence-derived) base regen amount. Unused by any built-in content today; kept for symmetry so equipment/buffs have the same seam Health already gets.</summary>
    ManaRegen,

    /// <summary>Damage an entity receives, consumed at HealthDamage.Apply -- the single chokepoint for every damage source (abilities, Burning, Poison, contact hazards) -- so a reduction here applies uniformly regardless of what dealt the damage.</summary>
    IncomingDamage,

    /// <summary>Chance (0..1) DamageEffectEntry rolls a crit, consumed via StatModifierMath.GetEffectiveValue against CritMath.BaseCritChance. Lets equipment/buffs (e.g. a stacking, self-granted "Double Tap" modifier) raise a caster's own crit chance the same generic way anything already modifies OutgoingDamage.</summary>
    CritChance,

    /// <summary>Multiplier DamageEffectEntry applies to a fully-scaled hit once CritChance rolls a crit, consumed via StatModifierMath.GetEffectiveValue against CritMath.BaseCritMultiplier.</summary>
    CritMultiplier,

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
