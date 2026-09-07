namespace Game.Modules;

/// <summary>Shared category vocabulary for both ActionDefinition and ItemDefinition -- an action
/// or item can carry several at once (e.g. Punch is Melee+Unarmed+Attack), so these drive
/// filtering/querying (see the "Dynamic per-tag inventory tabs" TODO) rather than which module a
/// definition lives in.</summary>
public enum Tag : byte
{
    Attack,
    Consumable,
    Equipment,
    Healing,
    Melee,
    Potion,
    Ranged,
    Scroll,
    Self,
    Spell,
    Tool,
    Unarmed,
    Weapon,
    Wand,
    Fire,

    /// <summary>Marks an action whose effects are skipped against a target currently holding DodgingComponent (see ActionEffectResolver.Apply) -- granted on PowerAttack/QuickAttack, withheld from auto-targeting/AOE effects (Magic Missile, explosions) per the Combat Overhaul: Dodge design.</summary>
    Dodgeable,

    /// <summary>Carried as the damageTags/activeTags on Poison's own DoT tick (PoisonSystem.Tick) -- lets a ConditionTag: Tag.Poison-scoped IncomingDamage modifier reduce poison damage specifically, the same generic mechanism Tag.Fire already gives Burning.</summary>
    Poison,

    // AbilityScoreType's 7 members, mirrored 1:1 -- lets an ability tagged with one of these get
    // a damage bonus from the matching ability score (see ActionEffectResolver.
    // ComputeAbilityScoreDamageBonus), the same generic tag-driven mechanism regardless of which
    // score actually applies to a given ability.
    Strength,
    Intelligence,
    Constitution,
    Dexterity,
    Charisma,
    Luck,
    Wisdom,
}
