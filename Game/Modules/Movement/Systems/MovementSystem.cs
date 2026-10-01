using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
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
        _movementDisabled = movementDisabled;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, processingTiers, processingTierEvents);
    }

    /// <summary>Attempts to execute the movement if a destination is set and nothing is gating the entity</summary>
    /// <remarks>
    /// All movement is gated by the ActionLock. Random movement is further gated by MovementComponent.WaitUntilFrame,
    /// a retry backoff for when a move fails due to a lack of available tiles. Both are deadlines -- this system
    /// compares them against the current frame and advances neither.
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
    /// Both gates are deadlines, not countdowns: the retry backoff is MovementComponent.WaitUntilFrame
    /// and the shared lock is ActionLockComponent.UnlockedAtFrame, both absolute frames compared
    /// against `now`, so the step needs no scaling by how often the entity's tier is visited.
    /// </remarks>
    private void UpdateEntity(int entityId, long now)
    {
        if (_deadEntities.Has(entityId) || _movementDisabled.Has(entityId))
        {
            return;
        }

        ref readonly var movementComponent = ref _movementComponents.GetReadonly(entityId);

        // The retry backoff is a deadline now (MovementComponent.WaitUntilFrame), so this is a pure
        // read -- no owner, nothing to advance, and no way for two systems to burn it down twice as
        // fast as intended the way they once did (see TestCombatBehaviorSystem's matching gate).
        if (movementComponent.IsWaiting(now))
        {
            return;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId, now) ||
            !_transformComponents.TryGetReadonly(entityId, out var transformComponent))
        {
            return;
        }

        if (!_mapQuery.IsOnMap(transformComponent.Position))
        {
            return;
        }

        var justSelected = movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value;
        if (justSelected)
        {
            ClearArrivedDestinationIfIdle(entityId);
        }

        if (movementComponent.NextMapPosition != null)
        {
            TryMoveToNextMapPosition(entityId, movementComponent, transformComponent, now);
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
    /// <param name="now">The current simulation frame -- a completed move locks the entity until a deadline measured from it.</param>
    private void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent, long now)
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
            var baseLockFrames = _actionLocks.GetReadonly(entityId).StandardLockFrames;
            var standardLockFrames = MathUtility.ClampUShort(
                StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MovementLockFrames, baseLockFrames),
                0,
                ushort.MaxValue);
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
}
