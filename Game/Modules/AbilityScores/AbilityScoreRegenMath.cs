namespace Game.Modules.AbilityScores;

/// <summary>
/// Shared regen-rate formula for both Health (Constitution) and Mana (Intelligence): a linear
/// ramp from 2%/second of the resource's effective maximum at ability score total 1, up to
/// 6%/second at total 300 (AbilityScoreMath's own clamp range). HealthRegenSystem/ManaRegenSystem
/// each convert the returned rate into a per-visit amount using their own tier cadence -- this
/// class only knows the rate, not frames.
/// </summary>
public static class AbilityScoreRegenMath
{
    private const float MinPercentPerSecond = 2f;
    private const float MaxPercentPerSecond = 6f;

    public static float ComputePercentPerSecond(short abilityScoreTotal) =>
        AbilityScoreMath.Lerp(abilityScoreTotal, MinPercentPerSecond, MaxPercentPerSecond);
}
