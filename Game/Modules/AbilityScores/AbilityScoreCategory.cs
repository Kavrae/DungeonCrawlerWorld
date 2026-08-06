namespace Game.Modules.AbilityScores;

/// <summary>
/// Core-vs-Hidden is a display/level-up-eligibility distinction only -- both categories share
/// the exact same AbilityScoreComponent storage. IsHidden checks against the Core set rather
/// than listing Hidden members directly: Core is fixed (5 scores, unlikely to grow), while
/// Hidden is expected to grow over time (see TODO.md's "split hidden ability scores into
/// composites" item) -- defining Hidden as "not Core" means a newly added hidden score is
/// correctly hidden by default, with no risk of this class being missed when AbilityScoreType
/// gains a member.
/// </summary>
public static class AbilityScoreCategory
{
    public static bool IsHidden(AbilityScoreType type) => !IsCore(type);

    private static bool IsCore(AbilityScoreType type) => type is
        AbilityScoreType.Strength or
        AbilityScoreType.Intelligence or
        AbilityScoreType.Constitution or
        AbilityScoreType.Dexterity or
        AbilityScoreType.Charisma;
}
