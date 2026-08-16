using Engine.ECS.Components;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.Poison;

namespace Game.Modules.Actions.Activators;

/// <summary>Provides effects and calculations for potion cooldowns.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class PotionCooldownEffects
{
    /// <summary>20s @ GameTiming.FramesPerSecond -- the cooldown at Constitution total 1, and ComputeDurationFrames' fallback when no AbilityScoreComponent is available.</summary>
    public const ushort DurationFrames = GameTiming.FramesPerSecond * 20;

    /// <summary>5s @ GameTiming.FramesPerSecond -- the cooldown at Constitution total 300 (AbilityScoreMath's own clamp range).</summary>
    public const ushort MinDurationFrames = GameTiming.FramesPerSecond * 5;

    /// <summary>Linear ramp from DurationFrames at Constitution total 1 down to MinDurationFrames at total 300 -- endpoints passed high-to-low since more Constitution means a shorter cooldown here (the inverse direction of HealthRegenSystem's own Constitution ramp).</summary>
    public static ushort ComputeDurationFrames(ushort constitutionTotal) =>
        (ushort)AbilityScoreMath.Lerp(constitutionTotal, DurationFrames, MinDurationFrames);

    /// <summary>Computes the duration of the abuse punishment Poison stack in ticks.</summary>
    /// <param name="durationFrames">The duration of the potion cooldown in frames.</param>
    /// <returns>The duration of the Poison stack in ticks.</returns>
    public static ushort ComputeAbusePoisonDurationTicks(ushort durationFrames) => (ushort)(durationFrames / PoisonEffects.TickIntervalFrames);

    /// <summary>Resets (or starts) the cooldown to full -- called on every successful potion consumption, whether or not one was already ticking down.</summary>
    public static void Reset(ComponentManager componentManager, int entityId, ushort durationFrames) =>
        componentManager.Merge(entityId, new PotionCooldownComponent(durationFrames, durationFrames));

    /// <summary>Whole seconds remaining, rounded up -- so the displayed number only reaches 0 once FramesRemaining actually does, rather than a moment early. Shared by every Presentation display of this cooldown (PlayerStatusEffectsContent, HotbarContent) so they can't disagree with each other.</summary>
    public static int RemainingSeconds(ushort framesRemaining) =>
        (int)System.Math.Ceiling(framesRemaining / (float)GameTiming.FramesPerSecond);
}
