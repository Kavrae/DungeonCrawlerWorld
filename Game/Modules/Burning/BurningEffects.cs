using Engine.ECS.Components;
using Engine.Events;
using Engine.Utilities;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>Burning's own rules: how many stacks it can hold, how often it ticks, and how a stack gets applied.</summary>
public static class BurningEffects
{
    public const byte MaxStacks = 20;

    /// <summary>Once per second -- literally GameTiming.FramesPerSecond, not a converted duration.</summary>
    public const ushort TickIntervalFrames = GameTiming.FramesPerSecond;

    /// <summary>🔥 (U+1F525, "fire"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "🔥";

    /// <summary>No-ops entirely if entityId is currently immune to Burning (StatusEffectImmunity), or once MaxStacks is reached.</summary>
    public static void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source, EventBus? eventBus = null, IPlayerQuery? playerQuery = null)
    {
        if (StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Burning, source, eventBus, playerQuery))
        {
            return;
        }

        var timers = componentManager.GetPackedPool<BurningTimerComponent>();

        if (timers.TryGetReadonly(entityId, out var existingTimer) && existingTimer.StackCount >= MaxStacks)
        {
            return;
        }

        componentManager.GetMultiPool<StatusEffectStack>().Add(entityId, new StatusEffectStack(StatusEffectType.Burning, source));

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, static (ref BurningTimerComponent t) => t.StackCount++);
        }
        else
        {
            timers.Add(entityId, new BurningTimerComponent(TickIntervalFrames, stackCount: 1));
        }
    }
}
