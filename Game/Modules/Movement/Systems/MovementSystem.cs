using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.Movement.Systems;

/// <summary>
/// Selects the next map node to path toward and moves the entity toward it based on its
/// movement mode. Depends on IMapQuery, not the concrete World, for collision/bounds reads. A
/// confirmed move is delivered three ways, matched to each consumer's actual need (see a
/// gameplay-demo profiling investigation that found the old single EventBus.Publish -- fanning
/// out to every subscriber, synchronously, per move, across a striped ~150k-entity population
/// -- was a measured hotspot):
/// - entityMoveSync.SyncMove: direct call, not the bus. Map-occupancy bookkeeping is
///   mandatory, not an optional module reaction, so it doesn't belong on EventBus at all (see
///   IEntityMoveSync's own doc comment).
/// - movedEntities.Record: a per-frame buffer ContactDamageSystem/StatusEffectAuraSystem drain
///   during their own Update, instead of each subscribing to a per-move event (see
///   FrameEventBuffer's own doc comment).
/// - eventBus.Publish(EntityMovedEvent), only for the player's own move: PlayerActivityLog still
///   subscribes to this on the bus exactly as before, just now firing at player-move frequency
///   (a handful/sec) instead of the full population's.
/// </summary>
public sealed class MovementSystem : ISystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private const short FramesToWaitIfNoOptions = 120;
    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    private readonly DirectComponentPool<TransformComponent> _transformComponents;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<MovementComponent> _movementComponents;
    private readonly IMapQuery _mapQuery;
    private readonly MathUtility _mathUtility;
    private readonly EventBus _eventBus;
    private readonly IEntityMoveSync _entityMoveSync;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        EventBus eventBus,
        IEntityMoveSync entityMoveSync,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _transformComponents = transformComponents;
        _actionLocks = actionLocks;
        _movementComponents = movementComponents;
        _mapQuery = mapQuery;
        _mathUtility = mathUtility;
        _eventBus = eventBus;
        _entityMoveSync = entityMoveSync;
        _movedEntities = movedEntities;
        _playerQuery = playerQuery;
        _deadEntities = deadEntities;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, processingTiers, processingTierEvents);
    }

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
                    movementComponent.FramesToWait = (short)Math.Max(0, movementComponent.FramesToWait - StripeCountValue);
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

            var justSelected = movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value;
            if (justSelected)
            {
                SetNextMapPosition(entityId, movementComponent, transformComponent);
            }

            if (movementComponent.NextMapPosition != null)
            {
                TryMoveToNextMapPosition(entityId, movementComponent, transformComponent, skipValidation: justSelected);
            }
        }
    }

    private void SetNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        if (movementComponent.MovementMode == MovementMode.Random)
        {
            if (_mathUtility.Next(0, 2) == 0)
            {
                SetIdle(entityId);
                return;
            }

            SetRandomMapPosition(entityId, movementComponent, transformComponent);
            return;
        }

        // TODO MovementMode.SeekTarget: path toward TargetMapPosition once pathfinding exists.

        // PlayerControlled (and SeekTarget until pathfinding exists) never auto-picks a next
        // destination -- that's an external caller's job (MapWindow.TryQueuePlayerMove).
        // Reaching this method at all means the entity just arrived at NextMapPosition (see
        // this method's only caller) with nothing new queued, so NextMapPosition must be
        // cleared here -- otherwise it stays set to the position the entity is already
        // standing on, and the very next Update call where the action lock allows it would
        // re-run TryMoveToNextMapPosition against that same value: a same-position "move" that
        // sets the action lock and publishes a spurious EntityMovedEvent(old == new) every cycle,
        // repeating forever instead of the entity going idle until a real move is queued. This
        // was previously harmless (only WorldEventSync/PlayerActivityLog consumed EntityMovedEvent,
        // both tolerant of Old == New) but became consequential once EntityMovedEvent-driven
        // systems (ContactDamageSystem, StatusEffectAuraSystem) started treating every such
        // event as a fresh step onto whatever tile the entity is on.
        _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);
    }

    /// <summary>
    /// Attempts to move toward the selected node. CanMove is re-checked here in case another
    /// entity has already moved into the space since it was selected -- except when
    /// skipValidation is set, meaning SetNextMapPosition just picked this exact position in
    /// this same synchronous call (the Random-mode path): nothing else can have touched the
    /// map between that pick and this call, so re-running CanMove (a full footprint walk for
    /// multi-tile entities) and IsBlocking would just recompute the identical answer. A
    /// position carried over from a previous frame (PlayerControlled's externally-queued
    /// moves) always gets the real re-check, since time -- and other entities' moves -- has
    /// actually passed since it was chosen. The action lock is set on the move itself, not
    /// during path selection.
    /// </summary>
    private void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent, bool skipValidation)
    {
        var newPosition = movementComponent.NextMapPosition!.Value;

        if (!skipValidation && !CanMove(newPosition, transformComponent.Size, entityId, _mapQuery.IsBlocking(entityId)))
        {
            _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);
            return;
        }

        var oldPosition = transformComponent.Position;

        if (_transformComponents.TryUpdate(entityId, newPosition, static (ref transformComponent, newPosition) =>
        {
            transformComponent.Position = newPosition;
        }))
        {
            ActionLockGate.Lock(_actionLocks, entityId, movementComponent.ActionCooldownFrames);

            var moved = new EntityMovedEvent(entityId, oldPosition, newPosition, transformComponent.Size);
            _entityMoveSync.SyncMove(moved);
            _movedEntities.Record(moved);

            if (entityId == _playerQuery?.PlayerEntityId)
            {
                _eventBus.Publish(moved);
            }
        }
    }

    /// <summary>
    /// Whether an entity of the given X/Y size could occupy the given position: every cell in
    /// its footprint must be on the map (see IMapQuery.IsOnMap(Vector3Int, Vector2Byte)) and
    /// either unoccupied or already occupied by itself. Bounds are checked first, since they
    /// have to be checked regardless and an out-of-bounds position never needs the occupancy
    /// work at all. Occupancy itself still has to be checked per cell (unlike bounds, a
    /// cell's occupancy can't be inferred from its neighbors' occupancy). isBlocking (see
    /// IMapQuery.IsBlocking) is the caller's to compute once and pass in, not this method's --
    /// it depends only on entityId, not on the candidate position being tested, so
    /// SetRandomMapPosition's retry loop would otherwise recompute the identical answer on
    /// every candidate direction it tries for the same entity. A non-Blocking mover skips the
    /// occupancy comparison entirely -- it's exempt from map collision, the same reason it
    /// never occupies the map's occupancy index in the first place (see World.IsBlocking) --
    /// but still can't move off the map.
    /// </summary>
    private bool CanMove(Vector3Int position, Vector2Byte size, int entityId, bool isBlocking)
    {
        if (!_mapQuery.IsOnMap(position, size))
        {
            return false;
        }

        if (!isBlocking)
        {
            return true;
        }

        if (size == TransformSize1)
        {
            var occupyingEntityId = _mapQuery.GetEntityIdAt(position);
            return occupyingEntityId == -1 || occupyingEntityId == entityId;
        }

        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                var occupyingEntityId = _mapQuery.GetEntityIdAt(new Vector3Int(x, y, position.Z));
                if (occupyingEntityId != -1 && occupyingEntityId != entityId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Picks a random neighboring node to move to. The node must be on the map and
    /// unoccupied. Directions immediately after the first failed attempt are slightly more
    /// likely to be selected than a uniform choice would give (see MathUtility.RandomExceptFor).
    /// A search that exhausts all four directions sets the entity's own FramesToWait, not the
    /// shared action lock -- failing to find a spot isn't an action, so it shouldn't consume
    /// the same budget a real move or a future melee attack would.
    /// </summary>
    private void SetRandomMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var isBlocking = _mapQuery.IsBlocking(entityId);
        var positionToTest = new Vector3Int();
        Span<int> failedIndexes = stackalloc int[4];
        var failedIndexCount = 0;

        if (transformComponent.Position.Y == 0)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.North;
        }
        else if (transformComponent.Position.Y == _mapQuery.MapSize.Y - size.Y)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.South;
        }
        if (transformComponent.Position.X == 0)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.East;
        }
        else if (transformComponent.Position.X == _mapQuery.MapSize.X - size.X)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.West;
        }

        do
        {
            var randomDirection = (Direction)_mathUtility.RandomExceptFor(4, failedIndexes[..failedIndexCount]);
            positionToTest = randomDirection switch
            {
                Direction.North => new Vector3Int(transformComponent.Position.X, transformComponent.Position.Y - 1, transformComponent.Position.Z),
                Direction.South => new Vector3Int(transformComponent.Position.X, transformComponent.Position.Y + 1, transformComponent.Position.Z),
                Direction.East => new Vector3Int(transformComponent.Position.X - 1, transformComponent.Position.Y, transformComponent.Position.Z),
                Direction.West => new Vector3Int(transformComponent.Position.X + 1, transformComponent.Position.Y, transformComponent.Position.Z),
                _ => positionToTest,
            };

            if (CanMove(positionToTest, size, entityId, isBlocking))
            {
                _movementComponents.TryUpdate(entityId, positionToTest, static (ref movementComponent, newPosition) =>
                {
                    movementComponent.NextMapPosition = newPosition;
                });
                return;
            }

            failedIndexes[failedIndexCount++] = (int)randomDirection;
        }
        while (failedIndexCount < 4);

        SetIdle(entityId);
    }

    /// <summary>Sets the entity's own FramesToWait (not the shared action lock -- going idle isn't an action) to FramesToWaitIfNoOptions.</summary>
    private void SetIdle(int entityId) =>
        _movementComponents.TryUpdate(entityId, FramesToWaitIfNoOptions, static (ref MovementComponent movementComponent, short framesToWait) =>
        {
            movementComponent.FramesToWait = framesToWait;
        });
}