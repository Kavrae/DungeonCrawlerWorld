using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Poison;

/// <summary>Poison's own rules: how many stacks it can hold, how often it ticks, and how a stack (with its own duration) gets applied.</summary>
public static class PoisonEffects
{
    // No explicit cap was specified for Poison -- mirrors Burning's cap as a reasonable default
    // (every stacking effect needs *some* limit; see the original stacking-status-effects spec).
    public const byte MaxStacks = byte.MaxValue;

    /// <summary>Once per second -- literally GameTiming.FramesPerSecond, not a converted duration.</summary>
    public const ushort TickIntervalFrames = GameTiming.FramesPerSecond;

    /// <summary>☠ (U+2620, "skull and crossbones"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "☠";

    /// <summary>
    /// No-ops entirely if entityId is currently immune to Poison (StatusEffectImmunity), or once
    /// MaxStacks is reached. durationInTicks is how many future damage applications this specific
    /// stack should keep Poison alive for -- scaled by the source's own OutgoingDebuffDuration
    /// (source.EntityId, when source is an entity) then the target's own IncomingDebuffDuration
    /// before being stored, unconditionally (no ConditionTag/activator-tags support here -- an
    /// aura-refreshed grant has no real activator to carry tags from; see this feature's own plan
    /// for why that's out of scope). The entity's overall RemainingDurationTicks becomes the
    /// greater of whatever it already was and this (already-scaled) value (never additive), so
    /// reapplying a long duration repeatedly keeps refreshing it, while a short reapplication after
    /// a longer one already landed does nothing to the timer.
    /// </summary>
    public static void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source, ushort durationInTicks, EventBus? eventBus = null, IPlayerQuery? playerQuery = null)
    {
        if (StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Poison, source, eventBus, playerQuery))
        {
            return;
        }

        var timers = componentManager.GetPackedPool<PoisonTimerComponent>();

        if (timers.TryGetReadonly(entityId, out var existingTimer) && existingTimer.StackCount >= MaxStacks)
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>() ? componentManager.GetMultiPool<StatModifierComponent>() : null;
        var scaledDuration = ScaleDebuffDuration(statModifiers, source, entityId, durationInTicks);

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, scaledDuration, static (ref PoisonTimerComponent t, ushort newDuration) =>
            {
                t.StackCount++;
                t.RemainingDurationTicks = Math.Max(t.RemainingDurationTicks, newDuration);
            });
        }
        else
        {
            timers.Add(entityId, new PoisonTimerComponent(TickIntervalFrames, stackCount: 1, remainingDurationTicks: scaledDuration, source));
        }
    }

    private static ushort ScaleDebuffDuration(MultiComponentPool<StatModifierComponent>? statModifiers, StatusEffectSource source, int targetEntityId, ushort durationInTicks)
    {
        var scaled = (float)durationInTicks;

        if (source.Kind == StatusEffectSourceKind.Entity)
        {
            scaled = StatModifierMath.GetEffectiveValue(statModifiers, source.EntityId, StatModifierTarget.OutgoingDebuffDuration, scaled);
        }

        scaled = StatModifierMath.GetEffectiveValue(statModifiers, targetEntityId, StatModifierTarget.IncomingDebuffDuration, scaled);
        return MathUtility.ClampUShort(scaled, 0, ushort.MaxValue);
    }
}
