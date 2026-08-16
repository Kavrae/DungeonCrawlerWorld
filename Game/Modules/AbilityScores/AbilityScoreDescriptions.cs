namespace Game.Modules.AbilityScores;

/// <summary>Short, flavor descriptions for the 5 Core ability scores, shown by the Ability Score window's hover tooltip.</summary>
/// <remarks>Hidden scores (Luck, Wisdom) are never shown to the player, so they're intentionally not covered here.</remarks>
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
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
