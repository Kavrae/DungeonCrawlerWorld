using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>Burning's own rules: how many stacks it can hold, how often it ticks, and how a stack gets applied.</summary>
public static class BurningEffects
{
    public const byte MaxStacks = 20;

    /// <summary>Once per second -- literally GameFrameRate.FramesPerSecond, not a converted duration.</summary>
    public const ushort TickIntervalFrames = GameFrameRate.FramesPerSecond;

    /// <summary>🔥 (U+1F525, "fire"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "🔥";

    /// <summary>Applies one stack -- see ApplyStacks.</summary>
    public static void ApplyStack(ComponentManager componentManager, int entityId, ActionSource source, long now, EventBus eventBus, IPlayerQuery playerQuery) =>
        ApplyStacks(componentManager, entityId, count: 1, source, now, eventBus, playerQuery);

    /// <summary>Adds up to count stacks, stopping at MaxStacks, and returns how many landed. None land on an entity currently immune to Burning (StatusEffectImmunity), reported as refused.</summary>
    /// <param name="now">The simulation frame the stacks land on. A new burn's first tick is TickIntervalFrames after it; a top-off leaves the running tick alone.</param>
    /// <param name="announcesRefusal">False to leave an immunity's blocked event unpublished.</param>
    /// <param name="immunityAlreadyChecked">True when the caller has just found the entity not immune: no second check.</param>
    public static StatusEffectStacksApplied ApplyStacks(ComponentManager componentManager, int entityId, int count, ActionSource source, long now, EventBus eventBus, IPlayerQuery playerQuery, bool announcesRefusal = true, bool immunityAlreadyChecked = false)
    {
        if (count <= 0)
        {
            return StatusEffectStacksApplied.Landed(0);
        }

        if (!immunityAlreadyChecked && StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Burning, source, eventBus, playerQuery, announcesRefusal))
        {
            return StatusEffectStacksApplied.Refused;
        }

        var timers = componentManager.GetPackedPool<BurningTimerComponent>();
        var hasTimer = timers.TryGetReadonly(entityId, out var existingTimer);
        var stacksLanded = Math.Min(count, MaxStacks - (hasTimer ? existingTimer.StackCount : 0));
        if (stacksLanded <= 0)
        {
            return StatusEffectStacksApplied.Landed(0);
        }

        if (hasTimer)
        {
            timers.TryUpdate(entityId, (byte)stacksLanded, static (ref BurningTimerComponent t, byte added) => t.StackCount += added);
        }
        else
        {
            timers.Add(entityId, new BurningTimerComponent(FrameDeadline.AfterStaggered(now, TickIntervalFrames, entityId), stackCount: (byte)stacksLanded, source));
        }

        return StatusEffectStacksApplied.Landed(stacksLanded);
    }
}
