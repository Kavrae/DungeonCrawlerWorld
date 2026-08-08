namespace Game.Modules;

/// <summary>Shared category vocabulary for both AbilityDefinition and ItemDefinition -- an ability
/// or item can carry several at once (e.g. Punch is Melee+Unarmed+Attack), so these drive
/// filtering/querying (see the "Dynamic per-tag inventory tabs" TODO) rather than which module a
/// definition lives in.</summary>
public enum Tag
{
    Attack,
    Consumable,
    Equipment,
    Healing,
    Melee,
    Potion,
    Ranged,
    Self,
    Spell,
    Tool,
    Unarmed,
    Weapon,

    // AbilityScoreType's 7 members, mirrored 1:1 -- lets an ability tagged with one of these get
    // a damage bonus from the matching ability score (see AbilityEffectResolver.
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
