using Engine.ECS.Components.Stores;
using Game.Effects;
using Game.Modules.Actions.Activators;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Tags;

namespace Game.Modules.Actions;

/// <summary>The one rule for whether an entity can use an action or item now, and how long until a usable action is ready.</summary>
/// <remarks>The activation systems refuse through it and ActionStateView answers Presentation through it, so what the player sees and what happens can't disagree.</remarks>
public static class ActivationQueries
{
    /// <summary>The first blocker that holds for entityId using definition now, or None.</summary>
    /// <remarks>
    /// Checked structural first: no activator, then melee disabled, then the activation effects (a cost among
    /// them) -- regaining mana wouldn't help an entity with no usable arms or hands. Turning a toggle off takes
    /// nothing, so one that is on is blocked by nothing -- not a cost, not disabled arms, not the Disabled status. Then the
    /// Disabled status (DisabledActivations), still before costs: regaining mana wouldn't help. The activation effects
    /// block with the first refusal's own blocker (BlockerFor). Anything reaching only its user -- an action or an item -- then asks its
    /// effects of that user too, so a use that would do nothing (a tome of a spell already known) is refused rather
    /// than spent.
    /// </remarks>
    /// <param name="isToggledOn">Whether the toggle is on now, so this use would turn it off. Ignored for a definition that isn't a toggle.</param>
    public static ActivationBlocker GetBlocker(
        int entityId,
        ActivatableDefinition definition,
        IActionActivator? activator,
        bool isToggledOn,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        EffectServices effectServices,
        long now)
    {
        if (activator is null)
        {
            return ActivationBlocker.NotActivatable;
        }

        if (definition.Tags.Has(GameTags.DeliveryMelee) && meleeDisabled.Has(entityId))
        {
            return ActivationBlocker.MeleeDisabled;
        }

        if (definition.Toggle is not null && isToggledOn)
        {
            return ActivationBlocker.None;
        }

        if (DisabledActivations.IsDisabled(effectServices.DisabledActivations, entityId, definition))
        {
            return ActivationBlocker.Disabled;
        }

        var activationRefusal = ActivationEffectsApplier.GetRefusal(effectServices, entityId, definition, now);
        if (activationRefusal != EffectRefusal.None || !activator.Targeting.IsSelfOnly)
        {
            return BlockerFor(activationRefusal);
        }

        return BlockerFor(EffectSequence.CanApply(definition.Effects, ActivationEffectsApplier.UserContext(effectServices, entityId, definition, now)));
    }

    /// <summary>The blocker an activation effect's refusal shows as.</summary>
    public static ActivationBlocker BlockerFor(EffectRefusal refusal) => refusal switch
    {
        EffectRefusal.None => ActivationBlocker.None,
        EffectRefusal.NoManaPool => ActivationBlocker.NoManaPool,
        EffectRefusal.NotEnoughMana => ActivationBlocker.NotEnoughMana,
        EffectRefusal.NoHealth => ActivationBlocker.NoHealth,
        EffectRefusal.NotEnoughHealth => ActivationBlocker.NotEnoughHealth,
        EffectRefusal.SpellAlreadyKnown => ActivationBlocker.SpellAlreadyKnown,
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Every EffectRefusal needs a blocker of its own."),
    };

    /// <summary>Frames until entityId could activate action at frame now: the later of its cooldown and, unless it's FreeCast, the action lock. 0 when ready.</summary>
    /// <remarks>An entity with no ActionLockComponent is never free of the lock (ActionLockGate.IsBlocked), so a lock-waiting action reads int.MaxValue for it.</remarks>
    public static int FramesUntilReady(
        int entityId,
        ActionDefinition action,
        EntityActions actions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        long now) =>
        FramesUntilReady(entityId, action.Activator.Timing, actions.CooldownFramesRemaining(entityId, action.Id, now), actionLocks, now);

    /// <summary>Frames until entityId could use something timed by timing whose own cooldown has cooldownFramesRemaining left: the later of that and, unless it's FreeCast, the action lock. 0 when ready.</summary>
    /// <remarks>The one rule an action and an item are both ready by; only where the cooldown is held differs (the entity's action, the item's unit).</remarks>
    public static int FramesUntilReady(
        int entityId,
        ActionTiming timing,
        int cooldownFramesRemaining,
        PackedComponentPool<ActionLockComponent> actionLocks,
        long now)
    {
        var framesUntilReady = cooldownFramesRemaining;

        if (timing.Category != ActionTimingCategory.FreeCast)
        {
            var lockFramesRemaining = actionLocks.TryGetReadonly(entityId, out var actionLock)
                ? ActionLockGate.FramesRemaining(actionLock, now)
                : int.MaxValue;
            framesUntilReady = System.Math.Max(framesUntilReady, lockFramesRemaining);
        }

        return framesUntilReady;
    }
}
