using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.World;

namespace Game.Modules.Movement.Systems;

/// <summary>Manages the movement of entities within the game world.</summary>
/// <remarks>
/// Movement uses an immediate eventbus dispatch model rather than the typical deferred event model to avoid race conditions that can occur with entity map placement.
/// 
/// Movement is divided into two phases : determining the next map tile and executing that movement.
/// 
/// Player movement is queued externally (Presentation input) while NPC wandering is decided upstream by TestCombatBehaviorSystem, which runs before this system each frame. This system only handles the actual movement and action lock timing.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MovementSystem : ISystem
{
    private const byte StripeCountValue = 15;

    /// <summary>√2 -- a diagonal step covers that much more distance than a cardinal one, so it sets the shared ActionLock for proportionally longer.</summary>
    private const float DiagonalActionLockMultiplier = 1.41421356f;

    public byte StripeCount => StripeCountValue;

    private readonly DirectComponentPool<TransformComponent> _transformComponents;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<MovementComponent> _movementComponents;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IEntityMoveSync _entityMoveSync;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntitiesEventBuffer;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<PendingActionActivationComponent>? _pendingActionActivations;
    private readonly PackedComponentPool<PendingConsumableActivationComponent>? _pendingConsumableActivations;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent>? _auraSources;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        EventBus eventBus,
        IEntityMoveSync entityMoveSync,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<PendingActionActivationComponent>? pendingActionActivations = null,
        PackedComponentPool<PendingConsumableActivationComponent>? pendingConsumableActivations = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null)
    {
        _transformComponents = transformComponents;
        _actionLocks = actionLocks;
        _movementComponents = movementComponents;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _entityMoveSync = entityMoveSync;
        _movedEntitiesEventBuffer = movedEntities;
        _playerQuery = playerQuery;
        _deadEntities = deadEntities;
        _pendingActionActivations = pendingActionActivations;
        _pendingConsumableActivations = pendingConsumableActivations;
        _auraSources = auraSources;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Decrements an entity's movement frames to wait or attempts to execute the movement if a destination is set</summary>
    /// <remarks>
    /// All movement is gated by the ActionLock. Random movement is further gated by the FramesToWait to account for a small waiting period 
    /// when a move fails due to a lack of available tiles.
    /// 
    /// Entities that are currently off the map cannot move and must instead be placed on the map by another system or event.
    /// </remarks>
    /// <param name="time">The engine time.</param>
    /// <param name="stripeIndex">The index of the entity stripe to update.</param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            ref readonly var movementComponent = ref _movementComponents.GetReadonly(entityId);

            if (movementComponent.FramesToWait > 0)
            {
                _movementComponents.TryUpdate(entityId, static (ref MovementComponent movementComponent) =>
                {
                    movementComponent.FramesToWait = MathUtility.DecrementClamped(movementComponent.FramesToWait, StripeCountValue);
                });
                continue;
            }

            if (ActionLockGate.IsBlocked(_actionLocks, entityId) ||
                !_transformComponents.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            if (!_mapQuery.IsOnMap(transformComponent.Position))
            {
                continue;
            }

            // Something upstream (TestCombatBehaviorSystem) already decided this entity's turn
            // this frame via a queued action/consumable activation -- don't also try to move
            // it. Requires TestCombatBehaviorSystem to run earlier in the frame (see
            // GameBootstrapper's module order) so this check sees the same-frame request.
            //TEMPORARY replace with a more generic mechanics
            if (_pendingActionActivations?.Has(entityId) == true || _pendingConsumableActivations?.Has(entityId) == true)
            {
                continue;
            }

            var justSelected = movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value;
            if (justSelected)
            {
                ClearArrivedDestinationIfIdle(entityId);
            }

            if (movementComponent.NextMapPosition != null)
            {
                TryMoveToNextMapPosition(entityId, movementComponent, transformComponent);
            }
        }
    }

    /// <summary>Clears the movement destination.</summary>
    /// <remarks>This is generally used for idle entities.</remarks>
    /// <param name="entityId">The ID of the entity.</param>
    private void ClearArrivedDestinationIfIdle(int entityId) =>
        _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);

    /// <summary>Attempts to move the entity to its next map position.</summary>
    /// <remarks>Blocking entities are gated by occupancy checks.</remarks>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="movementComponent">The movement component of the entity.</param>
    /// <param name="transformComponent">The transform component of the entity.</param>
    private void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        var newPosition = movementComponent.NextMapPosition!.Value;
        var oldPosition = transformComponent.Position;
        var isBlocking = _mapQuery.IsBlocking(entityId);
        var isDiagonal = newPosition.X != oldPosition.X && newPosition.Y != oldPosition.Y;

        if (!MovementCandidates.CanOccupy(_mapQuery, newPosition, transformComponent.Size, entityId, isBlocking) ||
            (isDiagonal && !MovementCandidates.IsDiagonalMoveClear(_mapQuery, oldPosition, newPosition, transformComponent.Size, entityId, isBlocking)))
        {
            _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);
            return;
        }

        if (_transformComponents.TryUpdate(entityId, newPosition, static (ref transformComponent, newPosition) =>
        {
            transformComponent.Position = newPosition;
        }))
        {
            var standardLockFrames = _actionLocks.GetReadonly(entityId).StandardLockFrames;
            var lockFrames = isDiagonal
                ? (ushort)MathF.Round(standardLockFrames * DiagonalActionLockMultiplier)
                : standardLockFrames;

            ActionLockGate.Lock(_actionLocks, entityId, lockFrames);

            var entityMovedEvent = new EntityMovedEvent(entityId, oldPosition, newPosition, transformComponent.Size);
            _entityMoveSync.SyncMove(entityMovedEvent);
            _movedEntitiesEventBuffer.Record(entityMovedEvent);

            if (entityId == _playerQuery?.PlayerEntityId || _auraSources?.Has(entityId) == true)
            {
                _eventBus.Publish(entityMovedEvent);
            }
        }
    }
}
