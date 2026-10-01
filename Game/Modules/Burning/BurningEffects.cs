using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Utilities;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;
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

    /// <summary>Applies one stack -- see ApplyStacks.</summary>
    public static void ApplyStack(ComponentManager componentManager, int entityId, ActionSource source, long now, EventBus eventBus, IPlayerQuery playerQuery) =>
        ApplyStacks(componentManager, entityId, count: 1, source, now, eventBus, playerQuery);

    /// <summary>Adds up to count stacks, stopping at MaxStacks, and returns how many landed. None land on an entity currently immune to Burning (StatusEffectImmunity).</summary>
    /// <param name="now">The simulation frame the stacks land on. A new burn's first tick is TickIntervalFrames after it; a top-off leaves the running tick alone.</param>
    /// <param name="announcesRefusal">False to leave an immunity's blocked event unpublished.</param>
    public static int ApplyStacks(ComponentManager componentManager, int entityId, int count, ActionSource source, long now, EventBus eventBus, IPlayerQuery playerQuery, bool announcesRefusal = true)
    {
        if (count <= 0 || StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Burning, source, eventBus, playerQuery, announcesRefusal))
        {
            return 0;
        }

        var timers = componentManager.GetPackedPool<BurningTimerComponent>();
        var hasTimer = timers.TryGetReadonly(entityId, out var existingTimer);
        var stacksLanded = Math.Min(count, MaxStacks - (hasTimer ? existingTimer.StackCount : 0));
        if (stacksLanded <= 0)
        {
            return 0;
        }

        if (hasTimer)
        {
            timers.TryUpdate(entityId, (byte)stacksLanded, static (ref BurningTimerComponent t, byte added) => t.StackCount += added);
        }
        else
        {
            timers.Add(entityId, new BurningTimerComponent(FrameDeadline.AfterStaggered(now, TickIntervalFrames, entityId), stackCount: (byte)stacksLanded, source));
        }

        return stacksLanded;
    }
}
