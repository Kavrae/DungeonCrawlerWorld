using Engine.ECS.Components.Stores;
using Game.Effects;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;

namespace Game.Modules.Actions;

/// <summary>One use of an action or an item, as the activation sequence (Activations.TryUse) sees it: the steps that differ between the two.</summary>
/// <remarks>Implemented by a struct, so the sequence is specialized per kind and nothing is allocated per use.</remarks>
public interface IActivationUse
{
    /// <summary>What is being used: the entity's effective action, or the stack's effective item.</summary>
    ActivatableDefinition Definition { get; }

    /// <summary>How it is timed.</summary>
    ActionTiming Timing { get; }

    /// <summary>Whether it is a toggle, so using it turns it on or off rather than applying its effects.</summary>
    bool IsToggle { get; }

    /// <summary>Whether it is a toggle that is on now, so this use turns it off.</summary>
    bool IsToggledOn { get; }

    /// <summary>Whether its own cooldown still holds at frame now.</summary>
    bool IsOnCooldown(long now);

    /// <summary>Starts its cooldown, if it has one, from frame now: before the use applies anything or flips, so an item flips or winds up the unit that is cooling.</summary>
    void StartCooldown(long now);

    /// <summary>Takes what using it uses up apart from its activation effects (an item's unit or charge). False, taking nothing, when there is nothing to take.</summary>
    bool TrySpend(long now);

    /// <summary>Applies its effects now, where its selection lands.</summary>
    void ApplyNow(long now);

    /// <summary>Turns the toggle on now.</summary>
    /// <remarks>The sequence has started its cooldown and taken its activation effects by then, so this cannot refuse: what could stop it is asked before (the blocker).</remarks>
    void TurnOn(long now);

    /// <summary>Turns the toggle off now.</summary>
    /// <remarks>The sequence has started its cooldown by then, so this cannot refuse.</remarks>
    void TurnOff(long now);

    /// <summary>The windup a Delayed use starts, holding the definition it resolves with.</summary>
    PendingWindupComponent CreateWindup();
}

/// <summary>The one activation sequence, run for every use of an action or an item.</summary>
/// <remarks>
/// Cooldown, blocker, lock, then what it spends, its cooldown starting, its activation effects and its timing step, then ReleasesActionLock --
/// so actions and items can differ only in what IActivationUse supplies. FreeCast neither waits for the action lock
/// nor sets it; Immediate waits for it, applies, and sets it; Delayed waits for it and winds up (Windups.Begin), and
/// the windup resolves with the definition it captured. A non-toggle use takes what it spends, then its activation
/// effects, before its timing step. A toggle that is off takes its activation effects and turns on (a Delayed one
/// winds up first); one that is on turns off and takes nothing -- both are timed by the toggle's own timing.
/// </remarks>
public sealed class Activations(
    PackedComponentPool<ActionLockComponent> actionLocks,
    PackedComponentPool<PendingWindupComponent> pendingWindups,
    PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
    EffectServices effectServices)
{
    /// <summary>Uses use for entityId at frame now if nothing refuses it. Returns whether it went ahead.</summary>
    public bool TryUse<TUse>(int entityId, ref TUse use, long now) where TUse : struct, IActivationUse
    {
        if (use.IsOnCooldown(now))
        {
            return false;
        }

        var definition = use.Definition;
        if (ActivationQueries.GetBlocker(entityId, definition, definition.ActivatorOrNone, use.IsToggledOn, meleeDisabled, effectServices, now) != ActivationBlocker.None)
        {
            return false;
        }

        var timing = use.Timing;
        if (timing.Category != ActionTimingCategory.FreeCast && ActionLockGate.IsBlocked(actionLocks, entityId, now))
        {
            return false;
        }

        if (use.IsToggle)
        {
            FlipToggle(entityId, ref use, timing, now);
        }
        else if (!TryApply(entityId, ref use, timing, now))
        {
            return false;
        }

        if (timing.ReleasesActionLock)
        {
            WindupCancel.TryCancel(pendingWindups, actionLocks, entityId, now, releaseLock: false);
            ActionLockGate.Release(actionLocks, entityId, now);
        }

        return true;
    }

    private bool TryApply<TUse>(int entityId, ref TUse use, ActionTiming timing, long now) where TUse : struct, IActivationUse
    {
        if (!use.TrySpend(now))
        {
            return false;
        }

        use.StartCooldown(now);
        ActivationEffectsApplier.Apply(effectServices, entityId, use.Definition, now);

        switch (timing.Category)
        {
            case ActionTimingCategory.Immediate:
                use.ApplyNow(now);
                ActionLockGate.Lock(actionLocks, entityId, now, LockFramesFor(entityId, timing));
                break;
            case ActionTimingCategory.Delayed:
                Windups.Begin(actionLocks, pendingWindups, entityId, now, LockFramesFor(entityId, timing), use.CreateWindup());
                break;
            case ActionTimingCategory.FreeCast:
                use.ApplyNow(now);
                break;
        }

        return true;
    }

    private void FlipToggle<TUse>(int entityId, ref TUse use, ActionTiming timing, long now) where TUse : struct, IActivationUse
    {
        use.StartCooldown(now);

        if (use.IsToggledOn)
        {
            use.TurnOff(now);
        }
        else
        {
            ActivationEffectsApplier.Apply(effectServices, entityId, use.Definition, now);

            if (timing.Category == ActionTimingCategory.Delayed)
            {
                Windups.Begin(actionLocks, pendingWindups, entityId, now, LockFramesFor(entityId, timing), use.CreateWindup());
                return;
            }

            use.TurnOn(now);
        }

        if (timing.Category != ActionTimingCategory.FreeCast)
        {
            ActionLockGate.Lock(actionLocks, entityId, now, LockFramesFor(entityId, timing));
        }
    }

    /// <summary>The lock the timing states, or the entity's standard lock when it states none.</summary>
    private ushort LockFramesFor(int entityId, ActionTiming timing) =>
        StandardActionLockFrames.ResolveForEntity(effectServices.AbilityScores, effectServices.StatModifiers, entityId, timing.ActionLockFrames);
}
