namespace Game.Modules.AbilityScores;

public enum AbilityScoreType
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
