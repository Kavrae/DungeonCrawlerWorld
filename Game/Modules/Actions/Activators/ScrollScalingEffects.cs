using Engine.Math;
using Game.Modules.AbilityScores;

namespace Game.Modules.Actions.Activators;

/// <summary>Provides scaling effects for scroll-based actions.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ScrollScalingEffects
{
    private const float MinMultiplier = 1.0f;
    private const float MaxMultiplier = 4.0f;

    public static float ComputeScaleMultiplier(ushort intelligenceTotal) =>
        AbilityScoreMath.Lerp(intelligenceTotal, MinMultiplier, MaxMultiplier);

    public static TargetingSpec ScaleTargeting(TargetingSpec baseTargeting, float multiplier) =>
        baseTargeting with
        {
            Range = (int)Math.Round(baseTargeting.Range * multiplier),
            AreaSize = (int)Math.Round(baseTargeting.AreaSize * multiplier),
        };
}
