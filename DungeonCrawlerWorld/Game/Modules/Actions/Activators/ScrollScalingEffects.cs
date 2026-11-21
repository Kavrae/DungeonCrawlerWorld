using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.Actions.Activators;

/// <summary>Provides scaling effects for scroll-based actions.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ScrollScalingEffects
{
    private const float MinMultiplier = 1.0f;
    private const float MaxMultiplier = 4.0f;

    /// <summary>The multiplier a scroll read by entityId scales by: its Intelligence total's, or 1 with no Intelligence score.</summary>
    public static float MultiplierFor(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId) =>
        AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Intelligence, out var intelligence)
            ? ComputeScaleMultiplier(intelligence.Total)
            : MinMultiplier;

    public static float ComputeScaleMultiplier(ushort intelligenceTotal) =>
        AbilityScoreMath.Lerp(intelligenceTotal, MinMultiplier, MaxMultiplier);

    public static TargetingSpec ScaleTargeting(TargetingSpec baseTargeting, float multiplier) =>
        baseTargeting with
        {
            MaximumRange = (int)Math.Round(baseTargeting.MaximumRange * multiplier),
            AreaSize = (int)Math.Round(baseTargeting.AreaSize * multiplier),
        };
}
