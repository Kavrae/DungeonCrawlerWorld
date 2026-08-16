using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
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
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly StatusEffectAuraApplierRegistry _statusEffectAppliers;
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<ManaComponent>? _mana;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent>? _hotkeyExpansionUnlocks;
    private readonly EntityStripeSet _stripeSet;

    public ActionActivationSystem(
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<ActionInstanceComponent> actionInstances,
        PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
        PackedComponentPool<HealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null)
    {
        _pendingActivations = pendingActivations;
        _actionLocks = actionLocks;
        _actionInstances = actionInstances;
        _pendingDelayedActions = pendingDelayedActions;
        _health = health;
        _statModifiers = statModifiers;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _mathUtility = mathUtility;
        _playerQuery = playerQuery;
        _statusEffectAppliers = statusEffectAppliers;
        _componentManager = componentManager;
        _deadEntities = deadEntities;
        _mana = mana;
        _abilityScores = abilityScores;
        _auraSources = auraSources;
        _hotkeyExpansionUnlocks = hotkeyExpansionUnlocks;

        _stripeSet = new EntityStripeSet(StripeCount, pendingActivations.EntityIds);
        pendingActivations.EntityAdded += _stripeSet.OnEntityAdded;
        pendingActivations.EntityRemoved += _stripeSet.OnEntityRemoved;
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
            if (_deadEntities?.Has(entityId) == true)
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

            if (!_actionCatalog.TryGet(request.ActionId, out var action) ||
                !ActionInstanceQueries.TryGet(_actionInstances, entityId, request.ActionId, out var instance))
            {
                continue;
            }

            if (instance.CooldownFramesRemaining > 0)
            {
                continue;
            }

            var manaCost = SpellActivator.ManaCostOf(action.Activator);
            if (!HasEnoughMana(entityId, manaCost))
            {
                continue;
            }

            var activationWasSuccessful = false;
            switch (action.Activator.Timing.Category)
            {
                case ActionTimingCategory.Immediate:
                    activationWasSuccessful = TryActivateImmediate(entityId, action, instance, request.TargetTiles);
                    break;
                case ActionTimingCategory.Delayed:
                    activationWasSuccessful = TryActivateDelayed(entityId, action, request.TargetTiles);
                    break;
                case ActionTimingCategory.FreeCast:
                    activationWasSuccessful = TryActivateFreeCast(entityId, action, instance, request.TargetTiles);
                    break;
            }
            if (activationWasSuccessful)
            {
                SpendManaIfAny(entityId, manaCost);
                StartCooldownIfAny(entityId, action);
            }
        }
    }

    private bool TryActivateImmediate(int entityId, ActionDefinition action, ActionInstanceComponent instance, Vector3Int[] targetTiles)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId))
        {
            return false;
        }

        ActionEffectResolver.Apply(action, instance, entityId, targetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks);
        ActionLockGate.Lock(_actionLocks, entityId, action.Activator.Timing.ActionLockFrames);
        return true;
    }

    private bool TryActivateDelayed(int entityId, ActionDefinition action, Vector3Int[] targetTiles)
    {
        if (ActionLockGate.IsBlocked(_actionLocks, entityId))
        {
            return false;
        }

        ActionLockGate.Lock(_actionLocks, entityId, action.Activator.Timing.ActionLockFrames);
        _pendingDelayedActions.Merge(entityId, new PendingDelayedActionComponent(action.Id, targetTiles));
        return true;
    }

    private bool TryActivateFreeCast(int entityId, ActionDefinition action, ActionInstanceComponent instance, Vector3Int[] targetTiles)
    {
        ActionEffectResolver.Apply(action, instance, entityId, targetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks);
        return true;
    }

    /// <summary>A ManaCost &lt;= 0 (the default) always passes, even with no ManaComponent pool registered at all -- most actions (e.g. Punch) never touch mana.</summary>
    private bool HasEnoughMana(int entityId, ushort manaCost)
    {
        if (manaCost <= 0)
        {
            return true;
        }

        return _mana is not null && _mana.TryGetReadonly(entityId, out var mana) && mana.CurrentMana >= manaCost;
    }

    private void SpendManaIfAny(int entityId, ushort manaCost)
    {
        if (manaCost > 0)
        {
            ManaSpend.Apply(_mana!, entityId, manaCost, _statModifiers);
        }
    }

    private void StartCooldownIfAny(int entityId, ActionDefinition action)
    {
        if (action.Activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            ActionInstanceQueries.TrySetCooldown(_actionInstances, entityId, action.Id, cooldownFrames);
        }
    }
}
