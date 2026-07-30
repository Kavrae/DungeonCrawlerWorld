using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.Abilities.Systems;

/// <summary>
/// Finishes a Delayed-category ability once its shared ActionLock windup ends: an entity with a
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
    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly AbilityCatalog _abilityCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly EntityStripeSet _stripeSet;

    public DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<AbilityInstanceComponent> abilityInstances,
        PackedComponentPool<HealthComponent> health,
        AbilityCatalog abilityCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        IPlayerQuery? playerQuery)
    {
        _pendingActions = pendingActions;
        _actionLocks = actionLocks;
        _abilityInstances = abilityInstances;
        _health = health;
        _abilityCatalog = abilityCatalog;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _playerQuery = playerQuery;

        _stripeSet = new EntityStripeSet(StripeCount, pendingActions.EntityIds);
        pendingActions.EntityAdded += _stripeSet.OnEntityAdded;
        pendingActions.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (!_pendingActions.TryGetReadonly(entityId, out var pending) ||
                !_actionLocks.TryGetReadonly(entityId, out var actionLock) ||
                actionLock.LockFramesRemaining > 0)
            {
                continue;
            }

            if (_abilityCatalog.TryGet(pending.AbilityId, out var ability) &&
                AbilityInstanceQueries.TryGet(_abilityInstances, entityId, pending.AbilityId, out var instance))
            {
                AbilityEffectResolver.Apply(ability, instance, entityId, pending.TargetTiles, _mapQuery, _health, _eventBus, _playerQuery);
            }

            _pendingActions.Remove(entityId);
        }
    }
}
