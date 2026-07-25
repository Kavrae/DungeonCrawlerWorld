using Engine.ECS.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Poison;

/// <summary>Poison's own rules: how many stacks it can hold, how often it ticks, and how a stack (with its own duration) gets applied.</summary>
public static class PoisonEffects
{
    // No explicit cap was specified for Poison -- mirrors Burning's cap as a reasonable default
    // (every stacking effect needs *some* limit; see the original stacking-status-effects spec).
    public const int MaxStacks = 20;
    public const int TickIntervalFrames = 60;

    /// <summary>☠ (U+2620, "skull and crossbones"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "☠";

    /// <summary>
    /// No-ops once MaxStacks is reached. durationInTicks is how many future damage
    /// applications this specific stack should keep Poison alive for -- the entity's overall
    /// RemainingDurationTicks becomes the greater of whatever it already was and this value
    /// (never additive), so reapplying a long duration repeatedly keeps refreshing it, while a
    /// short reapplication after a longer one already landed does nothing to the timer.
    /// </summary>
    public static void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source, int durationInTicks)
    {
        var timers = componentManager.GetPackedPool<PoisonTimerComponent>();

        if (timers.TryGetReadonly(entityId, out var existingTimer) && existingTimer.StackCount >= MaxStacks)
        {
            return;
        }

        componentManager.GetMultiPool<StatusEffectStack>().Add(entityId, new StatusEffectStack(StatusEffectType.Poison, source));

        if (timers.Has(entityId))
        {
            timers.TryUpdate(entityId, durationInTicks, static (ref PoisonTimerComponent t, int newDuration) =>
            {
                t.StackCount++;
                t.RemainingDurationTicks = Math.Max(t.RemainingDurationTicks, newDuration);
            });
        }
        else
        {
            timers.Add(entityId, new PoisonTimerComponent(TickIntervalFrames, stackCount: 1, remainingDurationTicks: durationInTicks, source));
        }
    }
}
