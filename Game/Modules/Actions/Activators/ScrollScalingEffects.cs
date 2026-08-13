using Engine.Math;
using Game.Modules.AbilityScores;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// A ScrollActivator's Range/AreaSize/duration all scale together off one multiplier derived
/// from the caster's Intelligence -- 100% at Intelligence 1 (or no score at all) up to 400% at
/// 300, mirroring PotionCooldownEffects.ComputeDurationFrames's use of AbilityScoreMath.Lerp but
/// low-to-high (more Intelligence should mean a bigger/longer scroll effect, the opposite
/// direction from Potion's shorter-cooldown ramp). Two independent consumers: Presentation/UI/
/// ActionTargetingController.TryGetArmedTargeting (Range/AreaSize, via ScaleTargeting, since
/// TargetShapeResolver.Resolve is called entirely in Presentation before ConsumableActivationSystem
/// ever sees a request) and ConsumableActivationSystem.ActivateScroll (duration, via the raw
/// multiplier on ActionEffectContext.DurationScaleMultiplier -- each duration-bearing effect
/// entry applies the multiplier to its own base value itself, the entry owns its own application
/// logic same as every other entry).
/// </summary>
public static class ScrollScalingEffects
{
    private const float MinMultiplier = 1.0f;
    private const float MaxMultiplier = 4.0f;

    public static float ComputeScaleMultiplier(short intelligenceTotal) =>
        AbilityScoreMath.Lerp(intelligenceTotal, MinMultiplier, MaxMultiplier);

    public static TargetingSpec ScaleTargeting(TargetingSpec baseTargeting, float multiplier) =>
        baseTargeting with
        {
            Range = (int)System.Math.Round(baseTargeting.Range * multiplier),
            AreaSize = (int)System.Math.Round(baseTargeting.AreaSize * multiplier),
        };
}
