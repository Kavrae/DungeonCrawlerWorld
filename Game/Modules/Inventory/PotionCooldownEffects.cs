using Engine.ECS.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Poison;

namespace Game.Modules.Inventory;

/// <summary>
/// Potion's global cooldown rules: set per consumer (an entity), not per item consumed -- e.g.
/// the player can only drink one potion per DurationFrames regardless of which potion type. Does
/// not gate a second potion; it only decides whether ConsumableActivationSystem also grants a
/// punishment Poison stack (see AbusePoisonDurationTicks) alongside that second potion's effect.
/// </summary>
public static class PotionCooldownEffects
{
    /// <summary>This codebase's standing frame-rate assumption (see PoisonEffects.TickIntervalFrames, AbilityTargetingController.DoubleTapWindowFrames) -- the one place it's named, rather than a bare "60" at every frames-to-seconds call site.</summary>
    public const int FramesPerSecond = 60;

    /// <summary>20s @ FramesPerSecond.</summary>
    public const short DurationFrames = FramesPerSecond * 20;

    /// <summary>
    /// The abuse-punishment Poison stack's duration, in PoisonSystem's own "ticks" (one tick per
    /// PoisonEffects.TickIntervalFrames real frames) -- derived from DurationFrames rather than a
    /// second hardcoded 20 so the punishment always lasts exactly as long as the cooldown itself,
    /// by construction, not by two constants happening to agree today.
    /// </summary>
    public const int AbusePoisonDurationTicks = DurationFrames / PoisonEffects.TickIntervalFrames;

    /// <summary>Resets (or starts) the cooldown to full -- called on every successful potion consumption, whether or not one was already ticking down.</summary>
    public static void Reset(ComponentManager componentManager, int entityId) =>
        componentManager.Merge(entityId, new PotionCooldownComponent(DurationFrames, DurationFrames));

    /// <summary>Whole seconds remaining, rounded up -- so the displayed number only reaches 0 once FramesRemaining actually does, rather than a moment early. Shared by every Presentation display of this cooldown (PlayerStatusEffectsContent, HotbarContent) so they can't disagree with each other.</summary>
    public static int RemainingSeconds(short framesRemaining) =>
        (int)System.Math.Ceiling(framesRemaining / (float)FramesPerSecond);
}
