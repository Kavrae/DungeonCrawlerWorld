using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Effects;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
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
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;
    private readonly EffectServices _effectServices;
    private readonly IMapQuery _mapQuery;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled;
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly ProcessingTierQuery _processingTiers;
    private readonly Toggles _toggles;
    private readonly TargetResolution _targetResolution;
    private readonly List<Vector3Int> _targetTilesScratch = [];
    private readonly EntityStripeSet _stripeSet;

    public ActionActivationSystem(
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        EntityActions actions,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        EffectServices effectServices,
        IMapQuery mapQuery,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        ProcessingTierQuery processingTiers,
        Toggles toggles,
        TargetResolution targetResolution)
    {
        _effectServices = effectServices;
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _actions = actions;
        _pendingWindups = pendingWindups;
        _mapQuery = mapQuery;
        _deadEntities = effectServices.DeadEntities;
        _meleeDisabled = meleeDisabled;
        _dodgingEntities = dodgingEntities;
        _processingTiers = processingTiers;
        _toggles = toggles;
        _targetResolution = targetResolution;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
    }

    /// <summary>Activate the pending actions for the entities in the specified entity stripe</summary>
    /// <remarks>
    /// Routes actions between the Immediate, Delayed, and FreeCast categories.
    /// Anything but FreeCast waits for the action lock; a use that is blocked or waiting takes nothing and starts no cooldown.
    /// A use that goes ahead takes its activation effects (a cost among them) first -- a Delayed one's when its windup starts -- unless it turns a toggle off.
    ///
    /// Immediate actions are applied immediately, and the action lock is applied.
    /// Delayed actions apply the action lock and are queued to activate at the end of it.
    /// FreeCast actions ignore the action lock and are activated immediately.
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

            var toggleKey = 0u;
            var isToggle = action.Toggle is not null;
            var isToggledOn = isToggle && _toggles.TryGetKey(entityId, ActivatableReference.Action(action.Id), out toggleKey);

            var now = time.FrameCount;
            if (ActivationQueries.GetBlocker(entityId, action, action.Activator, isToggledOn, _meleeDisabled, _effectServices, now) != ActivationBlocker.None)
            {
                continue;
            }

            var timing = action.Activator.Timing;
            if (timing.Category != ActionTimingCategory.FreeCast && ActionLockGate.IsBlocked(_actionLocks, entityId, now))
            {
                continue;
            }

            // Turning a toggle off takes nothing.
            if (!isToggledOn)
            {
                ActivationEffectsApplier.Apply(_effectServices, entityId, action, now);
            }

            if (isToggle)
            {
                FlipToggle(entityId, action, isToggledOn, toggleKey, request.Selection, now);
            }
            else
            {
                switch (timing.Category)
                {
                    case ActionTimingCategory.Immediate:
                        ApplyNow(entityId, action, request.Selection, now);
                        ActionLockGate.Lock(_actionLocks, entityId, now, LockFramesFor(entityId, timing));
                        break;
                    case ActionTimingCategory.Delayed:
                        Windups.Begin(_actionLocks, _pendingWindups, entityId, now, LockFramesFor(entityId, timing), PendingWindupComponent.ForAction(action.Id, request.Selection, readyAtFrame: 0));
                        break;
                    case ActionTimingCategory.FreeCast:
                        ApplyNow(entityId, action, request.Selection, now);
                        break;
                }
            }

            StartCooldownIfAny(entityId, action, now);

            if (timing.ReleasesActionLock)
            {
                WindupCancel.TryCancel(_pendingWindups, _actionLocks, entityId, now, releaseLock: false);
                ActionLockGate.Release(_actionLocks, entityId, now);
            }
        }
    }

    /// <summary>Switches a toggle action on or off in place of applying its effects: its Effects are what it holds while on (Toggles), and it applies to the entity itself.</summary>
    /// <remarks>
    /// The caller has checked the lock and taken the activation effects. A Delayed toggle that is off
    /// winds up instead (Windups.Begin) and goes on only when the windup resolves (DelayedActionSystem);
    /// turning one off is at once, as Immediate. FreeCast leaves the lock alone; anything else sets it.
    /// ActionActivatedEvent is published when the toggle actually flips, as for any action.
    /// </remarks>
    private void FlipToggle(int entityId, ActionDefinition action, bool isToggledOn, uint toggleKey, TargetSelection selection, long now)
    {
        var timing = action.Activator.Timing;
        if (!isToggledOn && timing.Category == ActionTimingCategory.Delayed)
        {
            Windups.Begin(_actionLocks, _pendingWindups, entityId, now, LockFramesFor(entityId, timing), PendingWindupComponent.ForAction(action.Id, selection, readyAtFrame: 0));
            return;
        }

        _effectServices.EventBus.Publish(new ActionActivatedEvent(entityId, action.Id));

        if (isToggledOn)
        {
            _toggles.TurnOff(entityId, toggleKey, action, now);
        }
        else
        {
            _toggles.TurnOn(entityId, ActivatableReference.Action(action.Id), action, now, placesAtHolderTile: selection.Mode == TargetingMode.Ground);
        }

        if (timing.Category != ActionTimingCategory.FreeCast)
        {
            ActionLockGate.Lock(_actionLocks, entityId, now, LockFramesFor(entityId, timing));
        }
    }

    /// <summary>The lock the action's timing states, or the entity's standard lock when it states none.</summary>
    private ushort LockFramesFor(int entityId, ActionTiming timing) =>
        StandardActionLockFrames.ResolveForEntity(_effectServices.AbilityScores, _effectServices.StatModifiers, entityId, timing.ActionLockFrames);

    /// <summary>Resolves the selection into tiles now and applies the action there: an Immediate or FreeCast activation lands where its target is at the moment it is confirmed, whatever the mode.</summary>
    private void ApplyNow(int entityId, ActionDefinition action, TargetSelection selection, long now)
    {
        var resolved = _targetResolution.Resolve(entityId, action.Activator.Targeting, selection, _targetTilesScratch);
        ActionEffectResolver.Apply(action, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, now, _dodgingEntities, _processingTiers);
    }

    private void StartCooldownIfAny(int entityId, ActionDefinition action, long now)
    {
        if (action.Activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            _actions.SetCooldown(entityId, action.Id, cooldownFrames, now);
        }
    }
}
