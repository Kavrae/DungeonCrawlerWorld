namespace Game.Modules.StatModifiers;

/// <summary> Which stat a StatModifierComponent affects. </summary>
/// <remarks>Extensible the same way StatusEffectType is -- new stats add new members here as something needs to modify them. </remarks>
/// <cleanupVersion>1</cleanupVersion>
public enum StatModifierTarget : byte
{
    OutgoingDamage,
    MaximumHealth,

    /// <summary>Layers on top of SimpleHealthRegenSystem's live-computed (Constitution-derived) base regen amount. Not a stored base value of its own; there's nothing left to modify in place, StatModifierMath.GetEffectiveValues is applied to the freshly-computed amount each visit.</summary>
    HealthRegen,

    MaximumMana,

    /// <summary>Mirrors HealthRegen -- layers on top of ManaRegenSystem's live-computed (Intelligence-derived) base regen amount. Unused by any built-in content today; kept for symmetry so equipment/buffs have the same seam Health already gets.</summary>
    ManaRegen,

    /// <summary>Damage an entity receives, consumed at HealthDamage.Apply -- the single chokepoint for every damage source (abilities, Burning, Poison, contact hazards) -- so a reduction here applies uniformly regardless of what dealt the damage. A modifier can scope itself to e.g. Tag.Melee via StatModifierComponent.ConditionTag rather than needing its own dedicated target (see that field's own doc comment) -- this is how the former, now-removed MeleeOutgoingDamage/melee-only-incoming special cases are expressed today.</summary>
    IncomingDamage,

    /// <summary>Chance (0..1) DirectDamage rolls a crit, consumed via StatModifierMath.GetEffectiveValue against CritMath.BaseCritChance. Lets equipment/buffs (e.g. a stacking, self-granted "Double Tap" modifier) raise a caster's own crit chance the same generic way anything already modifies OutgoingDamage.</summary>
    CritChance,

    /// <summary>Multiplier DirectDamage applies to a fully-scaled hit once CritChance rolls a crit, consumed via StatModifierMath.GetEffectiveValue against CritMath.BaseCritMultiplier.</summary>
    CritMultiplier,

    /// <summary>ActionLockComponent.StandardLockFrames' modifier seam -- consumed by MovementSystem.TryMoveToNextMapPosition. BodyPartEffectsSystem grants a multiplicative debuff here as an entity's own Leg/Foot body parts take damage (see PLAN-body-part-gameplay-effects.md); nothing else grants it yet, but it's an ordinary target like any other -- a future Dexterity/equipment consumer could layer on top the same way.</summary>
    MovementLockFrames,

    /// <summary>Heal amount a caster/source gives out, consumed at HealthHeal.Apply before IncomingHealing -- the healing counterpart to OutgoingDamage. A melee-only lifesteal-style heal buff would use ConditionTag: Tag.Melee here rather than a dedicated target.</summary>
    OutgoingHealing,

    /// <summary>Heal amount an entity receives, consumed at HealthHeal.Apply -- the healing counterpart to IncomingDamage, applying uniformly to every heal source unless scoped via ConditionTag.</summary>
    IncomingHealing,

    /// <summary>Scales a newly-granted Buff-polarity StatModifierComponent's own DurationFrames, consumed at StatModifierGrant.Apply against the caster (context.SourceEntityId) -- e.g. "buffs you cast on others last longer". Also scales a StatusEffectImmunityGrant's own DurationFrames, since granting immunity is unambiguously a Buff.</summary>
    OutgoingBuffDuration,

    /// <summary>Scales a newly-granted Buff-polarity StatModifierComponent's own DurationFrames, consumed at StatModifierGrant.Apply against the target (context.TargetEntityId) -- e.g. "buffs you receive last longer". Also scales a StatusEffectImmunityGrant's own DurationFrames.</summary>
    IncomingBuffDuration,

    /// <summary>Scales a newly-granted Debuff-polarity StatModifierComponent's own DurationFrames, consumed at StatModifierGrant.Apply against the caster (context.SourceEntityId) -- e.g. "debuffs you inflict last longer". Also scales Poison's own durationInTicks (PoisonEffects.ApplyStack) against the entity that applied it -- unconditionally there, since an aura-refreshed grant has no real activator to scope a ConditionTag against.</summary>
    OutgoingDebuffDuration,

    /// <summary>Scales a newly-granted Debuff-polarity StatModifierComponent's own DurationFrames, consumed at StatModifierGrant.Apply against the target (context.TargetEntityId) -- e.g. "debuffs against you expire faster". Also scales Poison's own durationInTicks (PoisonEffects.ApplyStack) against the entity it's landing on, unconditionally. Burning has no independent duration to scale (a stack's own decay -- 1 removed per tick -- is its only duration signal, and that same StackCount also drives its damage), so this never applies to Burning.</summary>
    IncomingDebuffDuration,

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
