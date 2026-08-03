using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Abilities.Systems;

/// <summary>
/// Consumes a PendingAbilityActivationComponent (queued by Presentation, never applied by it --
/// see that component's own doc comment) and dispatches by the ability's ActionTimingCategory:
/// Immediate applies its effect immediately then sets the shared ActionLock; Delayed sets the
/// lock first and hands off to DelayedActionSystem via PendingDelayedActionComponent; FreeCast
/// bypasses the shared lock entirely. AbilityTiming.CooldownFrames is not exclusive to
/// FreeCast, though -- any category may carry its own individual cooldown (see AbilityTiming's
/// own doc comment), so the cooldown gate is checked once, uniformly, before dispatching to any
/// of the three -- an Immediate (or Delayed) ability can end up gated by both the shared
/// ActionLock and a longer-lived cooldown of its own, not just one or the other. The request is
/// removed the same frame regardless of outcome -- a blocked/on-cooldown activation is simply
/// dropped, not queued to retry, since Presentation is expected to have already checked
/// targeting validity before ever writing the request; this system's own gate checks are
/// defense-in-depth against state changing between that check and this system's next visit.
/// </summary>
public sealed class AbilityActivationSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingAbilityActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly AbilityCatalog _abilityCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly StatusEffectAuraApplierRegistry _statusEffectAppliers;
    private readonly ComponentManager _componentManager;
    private readonly EntityStripeSet _stripeSet;

    public AbilityActivationSystem(
        PackedComponentPool<PendingAbilityActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<AbilityInstanceComponent> abilityInstances,
        PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
        PackedComponentPool<HealthComponent> health,
        AbilityCatalog abilityCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _abilityInstances = abilityInstances;
        _pendingDelayedActions = pendingDelayedActions;
        _health = health;
        _statModifiers = statModifiers;
        _abilityCatalog = abilityCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _statusEffectAppliers = statusEffectAppliers;
        _componentManager = componentManager;

        _stripeSet = new EntityStripeSet(StripeCount, pendingActivations.EntityIds);
        pendingActivations.EntityAdded += _stripeSet.OnEntityAdded;
        pendingActivations.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (!_pendingActivations.TryGetReadonly(entityId, out var request))
            {
                continue;
            }

            // Removed up front, not after dispatch -- every path below is a one-shot attempt,
            // so there's no outcome that should leave this request standing for a future visit.
            _pendingActivations.Remove(entityId);

            if (!_abilityCatalog.TryGet(request.AbilityId, out var ability) ||
                !AbilityInstanceQueries.TryGet(_abilityInstances, entityId, request.AbilityId, out var instance))
            {
                continue;
            }

            if (instance.CooldownFramesRemaining > 0)
            {
                continue;
            }

            var activationWasSuccessful = false;
            switch (ability.Timing.Category)
            {
                case ActionTimingCategory.Immediate:
                    activationWasSuccessful = TryActivateImmediate(entityId, ability, instance, request.TargetTiles);
                    break;
                case ActionTimingCategory.Delayed:
                    activationWasSuccessful = TryActivateDelayed(entityId, ability, request.TargetTiles);
                    break;
                case ActionTimingCategory.FreeCast:
                    activationWasSuccessful = TryActivateFreeCast(entityId, ability, instance, request.TargetTiles);
                    break;
            }
            if (activationWasSuccessful)
            {
                StartCooldownIfAny(entityId, ability);
            }
        }
    }

    private bool TryActivateImmediate(int entityId, AbilityDefinition ability, AbilityInstanceComponent instance, Vector3Int[] targetTiles)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId))
        {
            return false;
        }

        AbilityEffectResolver.Apply(ability, instance, entityId, targetTiles, _mapQuery, _health, _eventBus, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers);
        ActionLockGate.Lock(_actionLocks, entityId, ability.Timing.ActionLockFrames);
        return true;
    }

    private bool TryActivateDelayed(int entityId, AbilityDefinition ability, Vector3Int[] targetTiles)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId))
        {
            return false;
        }

        ActionLockGate.Lock(_actionLocks, entityId, ability.Timing.ActionLockFrames);
        _pendingDelayedActions.Merge(entityId, new PendingDelayedActionComponent(ability.Id, targetTiles));
        return true;
    }

    //Note : bool to account for future casting costs.
    private bool TryActivateFreeCast(int entityId, AbilityDefinition ability, AbilityInstanceComponent instance, Vector3Int[] targetTiles)
    {
        AbilityEffectResolver.Apply(ability, instance, entityId, targetTiles, _mapQuery, _health, _eventBus, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers);
        return true;
    }

    private void StartCooldownIfAny(int entityId, AbilityDefinition ability)
    {
        if (ability.Timing.CooldownFrames is { } cooldownFrames)
        {
            AbilityInstanceQueries.TrySetCooldown(_abilityInstances, entityId, ability.Id, cooldownFrames);
        }
    }
}
