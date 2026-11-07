using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Poison;

/// <summary>Poison's own rules: how many stacks it can hold, how often it ticks, and how long it lasts when a grant doesn't say.</summary>
public static class PoisonEffects
{
    // No explicit cap was specified for Poison -- mirrors Burning's cap as a reasonable default
    // (every stacking effect needs *some* limit; see the original stacking-status-effects spec).
    public const byte MaxStacks = byte.MaxValue;

    /// <summary>Once per second -- literally GameFrameRate.FramesPerSecond, not a converted duration.</summary>
    public const ushort TickIntervalFrames = GameFrameRate.FramesPerSecond;

    /// <summary>☠ (U+2620, "skull and crossbones"). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService).</summary>
    public const string Glyph = "☠";

    /// <summary>The ticks a grant naming no duration gives: two, so an aura re-granting it every second (TopUpTo) keeps it running while the entity stays exposed, and it ends at most two ticks after the entity leaves.</summary>
    /// <remarks>
    /// One would not be enough: a new poisoning's first tick lands up to one and a half intervals after it starts
    /// (FrameDeadline.AfterStaggered), so a single tick could run out before the next re-grant arrives. Never scaled: a
    /// debuff-duration modifier must not round an aura's refresh down to nothing.
    /// </remarks>
    public const ushort DefaultDurationTicks = 2;

    /// <summary>Adds up to count stacks lasting durationTicks, stopping at MaxStacks, and returns how many landed, or refused for an immune entity.</summary>
    /// <remarks>The duration is the greater of what the entity already had and durationTicks (TimedStatusEffectStacks), so reapplying a long one keeps refreshing it -- even at MaxStacks, or with count 0 -- and a short one after a longer does nothing to it.</remarks>
    /// <param name="durationTicks">Already scaled (StatusEffectGrant), or null for DefaultDurationTicks.</param>
    /// <param name="now">The simulation frame the stacks land on. A new poisoning's first tick is TickIntervalFrames after it; a re-application leaves the running tick alone.</param>
    /// <param name="announcesRefusal">False to leave an immunity's blocked event unpublished.</param>
    /// <param name="immunityAlreadyChecked">True when the caller has just found the entity not immune: no second check.</param>
    public static StatusEffectStacksApplied ApplyStacks(ComponentManager componentManager, int entityId, int count, ActionSource source, ushort? durationTicks, long now, EventBus eventBus, IPlayerQuery playerQuery, bool announcesRefusal = true, bool immunityAlreadyChecked = false)
    {
        if (!immunityAlreadyChecked && StatusEffectImmunity.IsImmune(componentManager, entityId, StatusEffectType.Poison, source, eventBus, playerQuery, announcesRefusal))
        {
            return StatusEffectStacksApplied.Refused;
        }

        return TimedStatusEffectStacks.Apply(componentManager.GetPackedPool<PoisonTimerComponent>(), entityId, count, MaxStacks, durationTicks ?? DefaultDurationTicks, source, now, TickIntervalFrames);
    }
}
