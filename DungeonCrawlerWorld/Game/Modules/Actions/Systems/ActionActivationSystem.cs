using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Effects;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>Consumes a PendingActionActivationComponent (queued by Presentation or an NPC) and uses the action through Activations.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ActionActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActivations;
    private readonly EntityActions _actions;
    private readonly EffectServices _effectServices;
    private readonly IMapQuery _mapQuery;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly ProcessingTierQuery _processingTiers;
    private readonly Toggles _toggles;
    private readonly TargetResolution _targetResolution;
    private readonly List<Vector3Int> _targetTilesScratch = [];
    private readonly HashSet<int> _reachedScratch = [];
    private readonly EntityStripeSet _stripeSet;
    private readonly Activations _activations;

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
        _actions = actions;
        _mapQuery = mapQuery;
        _deadEntities = effectServices.DeadEntities;
        _dodgingEntities = dodgingEntities;
        _processingTiers = processingTiers;
        _toggles = toggles;
        _targetResolution = targetResolution;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActivations);
        _activations = new Activations(actionLocks, pendingWindups, meleeDisabled, effectServices);
    }

    /// <summary>Activates the pending actions for the entities in the specified entity stripe.</summary>
    /// <remarks>Each request runs through Activations, the sequence an item's use runs through too: cooldown, blocker, lock, activation effects, then its timing.</remarks>
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

            var toggleKey = 0u;
            var isToggledOn = action.Toggle is not null && _toggles.TryGetKey(entityId, ActivatableReference.Action(action.Id), out toggleKey);
            var use = new ActionUse(this, entityId, action, request.Selection, isToggledOn, toggleKey);
            _activations.TryUse(entityId, ref use, time.FrameCount);
        }
    }

    /// <summary>Resolves the selection into tiles now and applies the action there: an Immediate or FreeCast activation lands where its target is at the moment it is confirmed, whatever the mode.</summary>
    private void ApplyNow(int entityId, ActionDefinition action, TargetSelection selection, long now)
    {
        var resolved = _targetResolution.Resolve(entityId, action.Activator.Targeting, selection, _targetTilesScratch);
        var everyTarget = default(EveryTargetRule);
        ActivationEffectResolver.Apply(action, ActivatableKind.Action, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, now, _dodgingEntities, _processingTiers, _reachedScratch, ref everyTarget);
    }

    /// <summary>A toggle action's flip: its Effects are what it holds while on (Toggles), applied to the entity itself. ActivationUsedEvent is published when it actually flips, as for any action.</summary>
    private void FlipToggle(int entityId, ActionDefinition action, bool turnOn, uint toggleKey, TargetSelection selection, long now)
    {
        _effectServices.EventBus.Publish(new ActivationUsedEvent(entityId, ActivatableKind.Action, action.Id, action.Tags));

        if (turnOn)
        {
            _toggles.TurnOn(entityId, ActivatableReference.Action(action.Id), action, now, placesAtHolderTile: selection.Mode == TargetingMode.Ground);
        }
        else
        {
            _toggles.TurnOff(entityId, toggleKey, action, now);
        }
    }

    /// <summary>An action's side of the activation sequence: its cooldown is the entity's for that action, it spends nothing but its activation effects, and it applies through ActivationEffectResolver.</summary>
    private readonly struct ActionUse(ActionActivationSystem system, int entityId, ActionDefinition action, TargetSelection selection, bool isToggledOn, uint toggleKey) : IActivationUse
    {
        public ActivatableDefinition Definition => action;

        public ActionTiming Timing => action.Activator.Timing;

        public bool IsToggle => action.Toggle is not null;

        public bool IsToggledOn => isToggledOn;

        public bool IsOnCooldown(long now) => system._actions.IsOnCooldown(entityId, action.Id, now);

        public void StartCooldown(long now)
        {
            if (action.Activator.Timing.CooldownFrames is { } cooldownFrames)
            {
                system._actions.SetCooldown(entityId, action.Id, cooldownFrames, now);
            }
        }

        public bool TrySpend(long now) => true;

        public void ApplyNow(long now) => system.ApplyNow(entityId, action, selection, now);

        public void TurnOn(long now) => system.FlipToggle(entityId, action, turnOn: true, toggleKey, selection, now);

        public void TurnOff(long now) => system.FlipToggle(entityId, action, turnOn: false, toggleKey, selection, now);

        public PendingWindupComponent CreateWindup() => PendingWindupComponent.ForAction(action, selection, readyAtFrame: 0);
    }
}
