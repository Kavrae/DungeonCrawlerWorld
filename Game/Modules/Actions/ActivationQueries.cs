using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.Actions.Activators;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Mana.Components;
using Game.Tags;

namespace Game.Modules.Actions;

/// <summary>The one rule for whether an entity can use an action or item now, and how long until a usable action is ready.</summary>
/// <remarks>The activation systems refuse through it and ActionStateView answers Presentation through it, so what the player sees and what happens can't disagree.</remarks>
public static class ActivationQueries
{
    /// <summary>The first blocker that holds for entityId using something with this activator and these tags, or None.</summary>
    /// <remarks>Checked structural first: no activator, then melee disabled, then mana -- regaining mana wouldn't help an entity with no usable arms or hands.</remarks>
    public static ActivationBlocker GetBlocker(
        int entityId,
        IActionActivator? activator,
        GameplayTagSet tags,
        PackedComponentPool<ManaComponent> mana,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled)
    {
        if (activator is null)
        {
            return ActivationBlocker.NotActivatable;
        }

        if (tags.Has(GameTags.DeliveryMelee) && meleeDisabled.Has(entityId))
        {
            return ActivationBlocker.MeleeDisabled;
        }

        var manaCost = SpellActivator.ManaCostOf(activator);
        if (manaCost > 0 && (!mana.TryGetReadonly(entityId, out var entityMana) || entityMana.CurrentMana < manaCost))
        {
            return ActivationBlocker.NotEnoughMana;
        }

        return ActivationBlocker.None;
    }

    /// <summary>Frames until entityId could activate action at frame now: the later of its cooldown and, unless it's FreeCast, the action lock. 0 when ready.</summary>
    /// <remarks>An entity with no ActionLockComponent is never free of the lock (ActionLockGate.IsBlocked), so a lock-waiting action reads int.MaxValue for it.</remarks>
    public static int FramesUntilReady(
        int entityId,
        ActionDefinition action,
        EntityActions actions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        long now)
    {
        var framesUntilReady = actions.CooldownFramesRemaining(entityId, action.Id, now);

        if (action.Activator.Timing.Category != ActionTimingCategory.FreeCast)
        {
            var lockFramesRemaining = actionLocks.TryGetReadonly(entityId, out var actionLock)
                ? ActionLockGate.FramesRemaining(actionLock, now)
                : int.MaxValue;
            framesUntilReady = System.Math.Max(framesUntilReady, lockFramesRemaining);
        }

        return framesUntilReady;
    }
}
