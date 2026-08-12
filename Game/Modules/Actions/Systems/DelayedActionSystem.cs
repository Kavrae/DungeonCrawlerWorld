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

/// <summary>
/// Finishes a Delayed-category action once its shared ActionLock windup ends: an entity with a
/// PendingDelayedActionComponent whose ActionLockComponent.LockFramesRemaining has reached 0
/// gets its effect resolved here, then the pending component is cleared so it isn't resolved
/// again next visit. Cancelling (right-click tap / Escape, Presentation layer) removes the
/// pending component directly and zeroes the lock -- this system never sees a cancelled action
/// at all, since there's nothing left for it to find once cancelled.
/// </summary>
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
    private readonly PackedComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
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
        PackedComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
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

        _stripeSet = new EntityStripeSet(StripeCount, pendingActions.EntityIds);
        pendingActions.EntityAdded += _stripeSet.OnEntityAdded;
        pendingActions.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

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
                actionLock.LockFramesRemaining > 0)
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
