using Engine.ECS.Components;
using Engine.Utilities;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Paralysis;

/// <summary>
/// Paralysis's own rules: how long it lasts and how a grant is applied. Locks the shared
/// ActionLockComponent for DurationFrames -- the same chokepoint MovementSystem and
/// ActionActivationSystem's Immediate/Delayed paths already gate through (see
/// ActionLockGate.IsBlocked), so movement and Immediate/Delayed ability activation are blocked
/// for free, with no gating code of Paralysis's own.
///
/// Deliberately does NOT block FreeCast: ActionActivationSystem.TryActivateFreeCast never
/// checks ActionLockGate.IsBlocked at all, so a paralyzed entity can still activate a
/// FreeCast-category ability. This is intentional, not a gap -- it's what lets a debuff-removal
/// spell/item (a FreeCast ability, usable during an Action Lock by design) actually be cast
/// while paralyzed, which is the entire point of a cleanse mechanic existing.
/// </summary>
public static class ParalysisEffects
{
    public static readonly short DurationFrames = (short)GameTiming.FramesForSeconds(5f);

    /// <summary>⚡ (U+26A1, "high voltage"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "⚡";

    /// <summary>
    /// Not a stacking effect -- reapplying while already active refreshes FramesUntilNextTick to
    /// the greater of what it already was and DurationFrames (never additive, same rule
    /// PoisonEffects.ApplyStack uses for its own duration), and never adds a second
    /// StatusEffectStack entry.
    /// </summary>
    public static void Apply(ComponentManager componentManager, int entityId, StatusEffectSource source)
    {
        var timers = componentManager.GetPackedPool<ParalysisTimerComponent>();

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, static (ref ParalysisTimerComponent t) =>
                t.FramesUntilNextTick = Math.Max(t.FramesUntilNextTick, DurationFrames));
        }
        else
        {
            componentManager.GetMultiPool<StatusEffectStack>().Add(entityId, new StatusEffectStack(StatusEffectType.Paralysis, source));
            timers.Add(entityId, new ParalysisTimerComponent(DurationFrames));
        }

        ActionLockGate.Lock(componentManager.GetPackedPool<ActionLockComponent>(), entityId, DurationFrames);
    }
}
