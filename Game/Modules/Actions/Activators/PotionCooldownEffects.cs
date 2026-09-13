using Engine.ECS.Components;
using Engine.ECS.Systems;
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

    /// <summary>Linear ramp from DurationFrames at Constitution total 1 down to MinDurationFrames at total 300 -- endpoints passed high-to-low since more Constitution means a shorter cooldown here (the inverse direction of SimpleHealthRegenSystem's own Constitution ramp).</summary>
    public static ushort ComputeDurationFrames(ushort constitutionTotal) =>
        (ushort)AbilityScoreMath.Lerp(constitutionTotal, DurationFrames, MinDurationFrames);

    /// <summary>Computes the duration of the abuse punishment Poison stack in ticks.</summary>
    /// <param name="durationFrames">The duration of the potion cooldown in frames.</param>
    /// <returns>The duration of the Poison stack in ticks.</returns>
    public static ushort ComputeAbusePoisonDurationTicks(ushort durationFrames) => (ushort)(durationFrames / PoisonEffects.TickIntervalFrames);

    /// <summary>Resets (or starts) the cooldown to full from frame now -- called on every successful potion consumption, whether or not one was already running.</summary>
    public static void Reset(ComponentManager componentManager, int entityId, ushort durationFrames, long now) =>
        componentManager.Merge(entityId, new PotionCooldownComponent(durationFrames, FrameDeadline.After(now, durationFrames)));

    /// <summary>Frames of cooldown left as of frame now; 0 once it has ended (even if PotionCooldownSystem hasn't removed the component yet this frame).</summary>
    public static int FramesRemaining(in PotionCooldownComponent cooldown, long now) =>
        FrameDeadline.Remaining(cooldown.ExpiresAtFrame, now);

    /// <summary>Whole seconds remaining, rounded up -- so the displayed number only reaches 0 once the cooldown actually has, rather than a moment early. Shared by every Presentation display of this cooldown (PlayerStatusEffectsContent, HotbarContent, HealthWindow) so they can't disagree with each other.</summary>
    public static int RemainingSeconds(int framesRemaining) =>
        (int)System.Math.Ceiling(framesRemaining / (float)GameTiming.FramesPerSecond);
}
