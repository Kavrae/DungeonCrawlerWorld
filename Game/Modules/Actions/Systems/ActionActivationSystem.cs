using Game.Effects;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary> Consumes a PendingActionActivationComponent (queued by Presentation) and dispatches by the action's ActionTimingCategory </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ActionActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly EntityActions _actions;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly EffectServices _effectServices;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<ManaComponent> _mana;
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled;
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly ProcessingTierQuery _processingTiers;
    private readonly EntityStripeSet _stripeSet;

    public ActionActivationSystem(
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        EntityActions actions,
        PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
        EffectServices effectServices,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        ProcessingTierQuery processingTiers)
    {
        _effectServices = effectServices;
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _actions = actions;
        _pendingDelayedActions = pendingDelayedActions;
        _statModifiers = effectServices.StatModifiers;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _deadEntities = effectServices.DeadEntities;
        _mana = effectServices.Mana;
        _meleeDisabled = meleeDisabled;
        _dodgingEntities = dodgingEntities;
        _processingTiers = processingTiers;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
    }

    /// <summary>Activate the pending actions for the entities in the specified entity stripe</summary>
    /// <remarks>
    /// Routes actions between the Immediate, Delayed, and QuickCast categories.
    /// Costs and cooldowns are not triggered unless the action is successful.
    /// 
    /// Immediate actions are gated by the action lock, applied immediately, and the action lock is applied.
    /// Delayed actions are gated by the action lock, the action lock is applied, and the action is queued to activate at the end of the action lock.
    /// QuickCast ignore the action lock and are are activated immediately.
    /// </remarks>
    /// <param name="time"></param>
    /// <param name="stripeIndex"></param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_deadEntities.Has(entityId))
            {
                _pendingActivations.Remove(entityId);
                continue;
            }

            if (!_pendingActivations.TryGetReadonly(entityId, out var request))
            {
                continue;
            }

            // Removed up front, not after dispatch -- every path below is a one-shot attempt,
            // so there's no outcome that should leave this request standing for a future visit.
            _pendingActivations.Remove(entityId);

            if (!_actions.TryGetEffectiveAction(entityId, request.ActionId, out var action))
            {
                continue;
            }

            if (_actions.IsOnCooldown(entityId, request.ActionId, time.FrameCount))
            {
                continue;
            }

            if (ActivationQueries.GetBlocker(entityId, action.Activator, action.Tags, _mana, _meleeDisabled) != ActivationBlocker.None)
            {
                continue;
            }

            var manaCost = SpellActivator.ManaCostOf(action.Activator);

            var activationWasSuccessful = false;
            switch (action.Activator.Timing.Category)
            {
                case ActionTimingCategory.Immediate:
                    activationWasSuccessful = TryActivateImmediate(entityId, action, request.TargetTiles, time.FrameCount);
                    break;
                case ActionTimingCategory.Delayed:
                    activationWasSuccessful = TryActivateDelayed(entityId, action, request.TargetTiles, time.FrameCount);
                    break;
                case ActionTimingCategory.FreeCast:
                    activationWasSuccessful = TryActivateFreeCast(entityId, action, request.TargetTiles, time.FrameCount);
                    break;
            }
            if (activationWasSuccessful)
            {
                SpendManaIfAny(entityId, manaCost);
                StartCooldownIfAny(entityId, action, time.FrameCount);

                if (action.Activator.Timing.ReleasesActionLock)
                {
                    WindupCancel.TryCancel(_pendingDelayedActions, _actionLocks, entityId, time.FrameCount, releaseLock: false);
                    ActionLockGate.Release(_actionLocks, entityId, time.FrameCount);
                }
            }
        }
    }

    private bool TryActivateImmediate(int entityId, ActionDefinition action, Vector3Int[] targetTiles, long now)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId, now))
        {
            return false;
        }

        ActionEffectResolver.Apply(action, entityId, targetTiles, _effectServices, _mapQuery, now, _dodgingEntities, _processingTiers);
        ActionLockGate.Lock(_actionLocks, entityId, now, action.Activator.Timing.ActionLockFrames);
        return true;
    }

    /// <summary>The windup's end is the lock's own deadline, read straight back off the component this just locked -- so the pending action and the lock can never disagree about when it resolves.</summary>
    private bool TryActivateDelayed(int entityId, ActionDefinition action, Vector3Int[] targetTiles, long now)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId, now))
        {
            return false;
        }

        ActionLockGate.Lock(_actionLocks, entityId, now, action.Activator.Timing.ActionLockFrames);

        // The fallback is unreachable in practice -- an entity with no ActionLockComponent reads as
        // blocked above, so it never gets here -- but it keeps the deadline honest if that ever changes.
        var readyAtFrame = _actionLocks.TryGetReadonly(entityId, out var actionLock)
            ? actionLock.UnlockedAtFrame
            : FrameDeadline.After(now, action.Activator.Timing.ActionLockFrames ?? 0);

        _pendingDelayedActions.Merge(entityId, new PendingDelayedActionComponent(action.Id, targetTiles, readyAtFrame));
        return true;
    }

    private bool TryActivateFreeCast(int entityId, ActionDefinition action, Vector3Int[] targetTiles, long now)
    {
        ActionEffectResolver.Apply(action, entityId, targetTiles, _effectServices, _mapQuery, now, _dodgingEntities, _processingTiers);
        return true;
    }

    private void SpendManaIfAny(int entityId, ushort manaCost)
    {
        if (manaCost > 0)
        {
            ManaSpend.Apply(_mana!, entityId, manaCost, _statModifiers);
        }
    }

    private void StartCooldownIfAny(int entityId, ActionDefinition action, long now)
    {
        if (action.Activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            _actions.SetCooldown(entityId, action.Id, cooldownFrames, now);
        }
    }
}
