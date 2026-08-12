using Engine.ECS.Components;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.Poison;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// PotionActivator's global cooldown rules: set per consumer (an entity), not per item consumed --
/// e.g. the player can only drink one potion per cooldown regardless of which potion type. Does
/// not gate a second potion; it only decides whether ConsumableActivationSystem also grants a
/// punishment Poison stack (see ComputeAbusePoisonDurationTicks) alongside that second potion's
/// effect. Lives alongside PotionActivator (not in Inventory) because this bookkeeping is a
/// property of having a PotionActivator-kind activation happen, not of inventory storage/stacking.
/// The cooldown itself scales with the consumer's own Constitution (ComputeDurationFrames) --
/// DurationFrames/MinDurationFrames are just its two endpoints, plus DurationFrames' own fallback
/// role when no Constitution score is available at all.
/// </summary>
public static class PotionCooldownEffects
{
    /// <summary>20s @ GameTiming.FramesPerSecond -- the cooldown at Constitution total 1, and ComputeDurationFrames' fallback when no AbilityScoreComponent is available.</summary>
    public const short DurationFrames = GameTiming.FramesPerSecond * 20;

    /// <summary>5s @ GameTiming.FramesPerSecond -- the cooldown at Constitution total 300 (AbilityScoreMath's own clamp range).</summary>
    public const short MinDurationFrames = GameTiming.FramesPerSecond * 5;

    /// <summary>Linear ramp from DurationFrames at Constitution total 1 down to MinDurationFrames at total 300 -- endpoints passed high-to-low since more Constitution means a shorter cooldown here (the inverse direction of AbilityScoreRegenMath's ramp).</summary>
    public static short ComputeDurationFrames(short constitutionTotal) =>
        (short)AbilityScoreMath.Lerp(constitutionTotal, DurationFrames, MinDurationFrames);

    /// <summary>
    /// The abuse-punishment Poison stack's duration, in PoisonSystem's own "ticks" (one tick per
    /// PoisonEffects.TickIntervalFrames real frames) -- derived from the same durationFrames the
    /// caller is about to pass to Reset, not a second hardcoded number, so the punishment always
    /// lasts exactly as long as the cooldown it's punishing, by construction, not by two values
    /// happening to agree today.
    /// </summary>
    public static int ComputeAbusePoisonDurationTicks(short durationFrames) => durationFrames / PoisonEffects.TickIntervalFrames;

    /// <summary>Resets (or starts) the cooldown to full -- called on every successful potion consumption, whether or not one was already ticking down.</summary>
    public static void Reset(ComponentManager componentManager, int entityId, short durationFrames) =>
        componentManager.Merge(entityId, new PotionCooldownComponent(durationFrames, durationFrames));

    /// <summary>Whole seconds remaining, rounded up -- so the displayed number only reaches 0 once FramesRemaining actually does, rather than a moment early. Shared by every Presentation display of this cooldown (PlayerStatusEffectsContent, HotbarContent) so they can't disagree with each other.</summary>
    public static int RemainingSeconds(short framesRemaining) =>
        (int)System.Math.Ceiling(framesRemaining / (float)GameTiming.FramesPerSecond);
}
