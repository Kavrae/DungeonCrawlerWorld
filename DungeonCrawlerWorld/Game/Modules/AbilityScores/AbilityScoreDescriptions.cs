namespace Game.Modules.AbilityScores;

/// <summary>Short, flavor descriptions for every ability score, shown by the Ability Score window's hover tooltip.</summary>
/// <remarks>Hidden scores (Luck, Wisdom) are never shown to the player normally -- their entries here only ever surface via the Ability Score window's admin-mode columns (see AbilityScoreWindow).</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class AbilityScoreDescriptions
{
    public static string Get(AbilityScoreType type) => type switch
    {
        AbilityScoreType.Strength => "Strength governs a crawler's raw physical power -- lifting and moving heavy objects, general athleticism, and the melee damage they deal. A crawler physically feels more powerful as the number climbs",
        AbilityScoreType.Intelligence => "Intelligence determines a crawler's maximum mana points (MP), their rate of mana regeneration, and their ability to comprehend spellbooks.",
        AbilityScoreType.Constitution => "Constitution measures a crawler's health and stamina. It is tied directly to their base health pool and natural healing speed. It also sets a crawler's \"potion cooldown\" -- how long they must wait between potions of any kind. Drinking a potion before the timer expires inflicts a Poison effect.",
        AbilityScoreType.Dexterity => "Dexterity measures a crawler's reaction time and is directly tied to how long they must wait between actions.",
        AbilityScoreType.Charisma => "Charisma shapes a crawler's social interactions, bargaining power, and charm abilities. It is tied to shop discounts, charm effects, and pet bonding.",
        AbilityScoreType.Luck => "Luck is hidden from ordinary play -- not yet tied to any mechanic (see TODO.md's Stats item).",
        AbilityScoreType.Wisdom => "Wisdom is hidden from ordinary play -- not yet tied to any mechanic (see TODO.md's Stats item).",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
