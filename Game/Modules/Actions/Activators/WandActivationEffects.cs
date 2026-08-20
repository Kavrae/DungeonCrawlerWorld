using Game.Modules.AbilityScores;

namespace Game.Modules.Actions.Activators;

/// <summary>Provides scaling effects for wand-based actions.</summary>
public static class WandActivationEffects
{
    /// <summary>Charge count at Intelligence total 1 -- the floor every wand grant starts from regardless of how low the recipient's Intelligence is.</summary>
    public const ushort MinCharges = 3;

    /// <summary>Charge count at Intelligence total 300 (AbilityScoreMath's own clamp range).</summary>
    public const ushort MaxCharges = 30;

    /// <summary>Linear ramp from MinCharges at Intelligence total 1 up to MaxCharges at total 300 -- mirrors ScrollScalingEffects/PotionCooldownEffects' own AbilityScoreMath.Lerp usage.</summary>
    public static ushort ComputeMaxCharges(ushort intelligenceTotal) => (ushort)AbilityScoreMath.Lerp(intelligenceTotal, MinCharges, MaxCharges);
}
