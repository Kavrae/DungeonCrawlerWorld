namespace Game.Modules.AbilityScores;

/// <summary>Represents the different types of ability scores.</summary>
/// <remarks>Both the 5 core ability scores and multiple hidden ones.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public enum AbilityScoreType : byte
{
    // Core -- player-visible, raisable by the future level-up process.
    Strength,
    Intelligence,
    Constitution,
    Dexterity,
    Charisma,

    // Hidden -- never shown to the player, never touched by level-up.
    Luck,
    Wisdom,
}
