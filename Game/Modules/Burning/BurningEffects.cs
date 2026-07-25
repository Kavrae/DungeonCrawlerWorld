using Engine.ECS.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>Burning's own rules: how many stacks it can hold, how often it ticks, and how a stack gets applied.</summary>
public static class BurningEffects
{
    public const int MaxStacks = 20;
    public const int TickIntervalFrames = 60;

    /// <summary>
    /// Requires DroidSansJapanese.ttf loaded as a fallback font (see FontService).
    /// </summary>
    public const string Glyph = "火";

    public static void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source)
    {
        var timers = componentManager.GetPackedPool<Components.BurningTimerComponent>();

        if (timers.TryGetReadonly(entityId, out var existingTimer) && existingTimer.StackCount >= MaxStacks)
        {
            return;
        }

        componentManager.GetMultiPool<StatusEffectStack>().Add(entityId, new StatusEffectStack(StatusEffectType.Burning, source));

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, static (ref Components.BurningTimerComponent t) => t.StackCount++);
        }
        else
        {
            timers.Add(entityId, new Components.BurningTimerComponent(TickIntervalFrames, stackCount: 1));
        }
    }
}
