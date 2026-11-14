using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Movement.Systems;

/// <summary>Manages the movement of entities within the game world.</summary>
/// <remarks>
/// Movement uses an immediate eventbus dispatch model rather than the typical deferred event model to avoid race conditions that can occur with entity map placement.
/// 
/// Movement is divided into two phases : determining the next map tile and executing that movement.
/// 
/// Player movement is queued externally (Presentation input) while NPC wandering is decided upstream by NpcDecisionSystem, which runs before this system each frame. This system only handles the actual movement and action lock timing.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MovementSystem : ITieredSystem
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
    private readonly IPlayerQuery _playerQuery;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<AbilityScoresComponent> _abilityScores;
    private readonly PackedComponentPool<MovementDisabledComponent> _movementDisabled;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        EventBus eventBus,
        IEntityMoveSync entityMoveSync,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        IPlayerQuery playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent> deadEntities,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<AbilityScoresComponent> abilityScores,
        PackedComponentPool<MovementDisabledComponent> movementDisabled)
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
        _statModifiers = statModifiers;
        _abilityScores = abilityScores;
        _movementDisabled = movementDisabled;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Attempts to execute the movement if a destination is set and nothing is gating the entity</summary>
    /// <remarks>
    /// All movement is gated by the ActionLock, a deadline this system compares against the current frame and never advances.
    /// 
    /// Entities that are currently off the map cannot move and must instead be placed on the map by another system or event.
    /// </remarks>
    /// <param name="time">The engine time.</param>
    /// <param name="stripeIndex">The index of the entity stripe to update.</param>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Moves the player every frame, so a step the player asked for is taken on the frame it can be rather than on their stripe's next visit.</summary>
    /// <remarks>UpdateBucket skips the player, so the player is still handled exactly once per frame.</remarks>
    public void BeginFrame(EngineTime time)
    {
        var playerEntityId = _playerQuery.PlayerEntityId;
        if (_movementComponents.Has(playerEntityId))
        {
            UpdateEntity(playerEntityId, time.FrameCount);
        }
    }

    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        var playerEntityId = _playerQuery.PlayerEntityId;
        foreach (var entityId in entityIds)
        {
            if (entityId != playerEntityId)
            {
                UpdateEntity(entityId, time.FrameCount);
            }
        }
    }

    /// <summary>One due entity's movement step.</summary>
    /// <remarks>
    /// An entity with no step costs one lookup. A step that can never be taken -- the entity is dead,
    /// movement-disabled or off the map, has already arrived, or the step is not to an adjacent tile on
    /// its own layer -- is cleared, so whatever decides the next one is never left waiting on it.
    /// The shared lock (ActionLockComponent.UnlockedAtFrame) is a deadline compared against now; a step waiting on
    /// it is kept.
    /// </remarks>
    private void UpdateEntity(int entityId, long now)
    {
        ref readonly var movementComponent = ref _movementComponents.GetReadonly(entityId);
        if (movementComponent.NextMapPosition is not { } nextPosition)
        {
            return;
        }

        if (_deadEntities.Has(entityId) ||
            _movementDisabled.Has(entityId) ||
            !_transformComponents.TryGetReadonly(entityId, out var transformComponent) ||
            !_mapQuery.IsOnMap(transformComponent.Position))
        {
            ClearStep(entityId);
            return;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId, now))
        {
            return;
        }

        TryMoveToNextMapPosition(entityId, nextPosition, transformComponent, now);
    }

    private void ClearStep(int entityId) =>
        _movementComponents.TryUpdate(entityId, new SetNextMapPositionUpdate(null));

    /// <summary>Attempts to move the entity to newPosition by the step rule (MovementCandidates.CanStep), and clears the step whether or not it moves.</summary>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="newPosition">The tile to step to.</param>
    /// <param name="transformComponent">The transform component of the entity.</param>
    /// <param name="now">The current simulation frame -- a completed move locks the entity until a deadline measured from it.</param>
    private void TryMoveToNextMapPosition(int entityId, Vector3Int newPosition, TransformComponent transformComponent, long now)
    {
        var oldPosition = transformComponent.Position;
        var isBlocking = _mapQuery.IsBlocking(entityId);
        var isDiagonal = newPosition.X != oldPosition.X && newPosition.Y != oldPosition.Y;

        ClearStep(entityId);

        if (!MovementCandidates.CanStep(_mapQuery, oldPosition, newPosition, transformComponent.Size, entityId, isBlocking))
        {
            return;
        }

        using (var movedTransform = _transformComponents.TryWrite(entityId))
        {
            if (!movedTransform.Found)
            {
                return;
            }

            movedTransform.Value.Position = newPosition;
        }

        var standardLockFrames = StandardActionLockFrames.ResolveMovementForEntity(_abilityScores, _statModifiers, entityId);
        var lockFrames = isDiagonal
            ? (ushort)MathF.Round(standardLockFrames * DiagonalActionLockMultiplier)
            : standardLockFrames;

        ActionLockGate.Lock(_actionLocks, entityId, now, lockFrames);

        var entityMovedEvent = new EntityMovedEvent(entityId, oldPosition, newPosition, transformComponent.Size);
        _entityMoveSync.SyncMove(entityMovedEvent, isBlocking);
        _movedEntitiesEventBuffer.Record(entityMovedEvent);

        if (entityId == _playerQuery.PlayerEntityId)
        {
            _eventBus.Publish(entityMovedEvent);
        }
    }
}
