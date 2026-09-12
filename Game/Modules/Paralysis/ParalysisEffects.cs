using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Utilities;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis.Components;
using Game.Modules.StatusEffects;
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
    public static readonly ushort DurationFrames = GameTiming.FramesForSeconds(5f);

    /// <summary>⚡ (U+26A1, "high voltage"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "⚡";

    /// <summary>
    /// No-ops entirely if entityId is currently immune to Paralysis (StatusEffectImmunity).
    /// Otherwise not a stacking effect -- reapplying while already active pushes the expiry out to
    /// the later of what it already was and DurationFrames from now (never additive, same rule
    /// PoisonEffects.ApplyStack uses for its own duration).
    /// </summary>
    /// <param name="now">The simulation frame Paralysis is applied on.</param>
    public static void Apply(ComponentManager componentManager, int entityId, StatusEffectSource source, long now, EventBus? eventBus = null, IPlayerQuery? playerQuery = null)
    {
        if (StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Paralysis, source, eventBus, playerQuery))
        {
            return;
        }

        var timers = componentManager.GetPackedPool<ParalysisTimerComponent>();
        var expiresAtFrame = FrameDeadline.After(now, DurationFrames);

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, expiresAtFrame, static (ref ParalysisTimerComponent t, uint expires) =>
                t.ExpiresAtFrame = Math.Max(t.ExpiresAtFrame, expires));
        }
        else
        {
            timers.Add(entityId, new ParalysisTimerComponent(expiresAtFrame));
        }

        ActionLockGate.Lock(componentManager.GetPackedPool<ActionLockComponent>(), entityId, now, DurationFrames);
    }
}
