using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>Consumes pending delayed actions and resolves their effects when the action lock is released.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DelayedActionSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingActions;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly StatusEffectAuraApplierRegistry _statusEffectAppliers;
    private readonly ComponentManager _componentManager;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly MultiComponentPool<AbilityScoreComponent>? _abilityScores;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent>? _hotkeyExpansionUnlocks;
    private readonly EntityStripeSet _stripeSet;

    public DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<ActionInstanceComponent> actionInstances,
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
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null)
    {
        _pendingActions = pendingActions;
        _actionLocks = actionLocks;
        _actionInstances = actionInstances;
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
        _abilityScores = abilityScores;
        _auraSources = auraSources;
        _hotkeyExpansionUnlocks = hotkeyExpansionUnlocks;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, pendingActions);
    }

    /// <summary>Updates the delayed actions for the entities in the specified entity stripe</summary>
    /// <remarks>
    /// Delayed actions are resolved when the action lock is released.
    /// Each delayed action sets its own action lock duration.
    /// </remarks>
    /// <param name="time">The current engine time</param>
    /// <param name="stripeIndex">The index of the entity stripe to update</param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                _pendingActions.Remove(entityId);
                continue;
            }

            if (!_pendingActions.TryGetReadonly(entityId, out var pending) ||
                !_actionLocks.TryGetReadonly(entityId, out var actionLock) ||
                actionLock.CurrentLockFramesRemaining > 0)
            {
                continue;
            }

            if (_actionCatalog.TryGet(pending.ActionId, out var action) &&
                ActionInstanceQueries.TryGet(_actionInstances, entityId, pending.ActionId, out var instance))
            {
                ActionEffectResolver.Apply(action, instance, entityId, pending.TargetTiles, _mapQuery, _health, _eventBus, _mathUtility, _playerQuery, _statusEffectAppliers, _componentManager, _statModifiers, _deadEntities, _abilityScores, _auraSources, _hotkeyExpansionUnlocks);
            }

            _pendingActions.Remove(entityId);
        }
    }
}
