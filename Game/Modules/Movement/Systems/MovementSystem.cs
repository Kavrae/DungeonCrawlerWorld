using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;
using Game.Modules.Movement.Components;
using Game.World;

namespace Game.Modules.Movement.Systems;

/// <summary>
/// Selects the next map node to path toward and moves the entity toward it based on its
/// movement mode. Depends on IMapQuery, not the concrete World, for collision/bounds reads;
/// a confirmed move publishes EntityMoved (immediate dispatch) rather than calling into
/// World directly -- WorldEventSync is the only thing that still touches World.MoveEntity.
/// </summary>
public sealed class MovementSystem : ISystem
{
    public byte StripeCount => 15;

    private const short FramesToWaitIfNoOptions = 10;
    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    private readonly DirectComponentPool<TransformComponent> _transformComponents;
    private readonly PackedComponentPool<EnergyComponent> _energyComponents;
    private readonly PackedComponentPool<MovementComponent> _movementComponents;
    private readonly IMapQuery _mapQuery;
    private readonly MathUtility _mathUtility;
    private readonly EventBus _eventBus;
    private readonly EntityStripeSet _stripeSet;

    public MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<EnergyComponent> energyComponents,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        EventBus eventBus)
    {
        _transformComponents = transformComponents;
        _energyComponents = energyComponents;
        _movementComponents = movementComponents;
        _mapQuery = mapQuery;
        _mathUtility = mathUtility;
        _eventBus = eventBus;

        _stripeSet = new EntityStripeSet(StripeCount, movementComponents.EntityIds);
        movementComponents.EntityAdded += _stripeSet.OnEntityAdded;
        movementComponents.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (!_energyComponents.Has(entityId) || !_transformComponents.Has(entityId))
            {
                continue;
            }

            ref readonly var movementComponent = ref _movementComponents.GetReadonly(entityId);
            if (movementComponent.FramesToWait > 0)
            {
                _movementComponents.TryUpdate(entityId, static (ref movementComponent) =>
                {
                    movementComponent.FramesToWait -= 1;
                });
                continue;
            }

            ref readonly var transformComponent = ref _transformComponents.GetReadonly(entityId);
            if (!_mapQuery.IsOnMap(transformComponent.Position))
            {
                continue;
            }

            ref readonly var energyComponent = ref _energyComponents.GetReadonly(entityId);
            if (energyComponent.CurrentEnergy < movementComponent.EnergyToMove)
            {
                continue;
            }

            if (movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value)
            {
                SetNextMapPosition(entityId, movementComponent, transformComponent);
            }

            if (movementComponent.NextMapPosition != null)
            {
                TryMoveToNextMapPosition(entityId, movementComponent, transformComponent);
            }
        }
    }

    private void SetNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        if (movementComponent.MovementMode == MovementMode.Random)
        {
            SetRandomMapPosition(entityId, movementComponent, transformComponent);
        }
        // TODO MovementMode.SeekTarget: path toward TargetMapPosition once pathfinding exists.
    }

    /// <summary>
    /// Attempts to move toward the selected node. CanMove is re-checked here in case
    /// another entity has already moved into the space since it was selected. Energy is
    /// consumed on the move itself, not during path selection.
    /// </summary>
    private void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        var oldPosition = transformComponent.Position;
        var newPosition = movementComponent.NextMapPosition!.Value;

        _transformComponents.TryUpdate(entityId, newPosition, static (ref transformComponent, newPosition) =>
        {
            transformComponent.Position = newPosition;
        });

        _energyComponents.TryUpdate(entityId, movementComponent.EnergyToMove, static (ref energyComponent, energyToMove) =>
        {
            energyComponent.CurrentEnergy -= energyToMove;
        });

        _eventBus.Publish(new EntityMoved(entityId, oldPosition, newPosition, transformComponent.Size));
    }

    /// <summary>
    /// Whether an entity of the given X/Y size could occupy the given position: every cell in
    /// its footprint must be on the map (see IMapQuery.IsOnMap(Vector3Int, Vector2Byte)) and
    /// either unoccupied or already occupied by itself. Bounds are checked first, since they
    /// have to be checked regardless and an out-of-bounds position never needs the occupancy
    /// work at all. Occupancy itself still has to be checked per cell (unlike bounds, a
    /// cell's occupancy can't be inferred from its neighbors' occupancy). A non-Blocking
    /// mover (IMapQuery.IsBlocking) skips the occupancy comparison entirely -- it's exempt
    /// from map collision, the same reason it never occupies the map's occupancy index in the
    /// first place (see World.IsBlocking) -- but still can't move off the map.
    /// </summary>
    private bool CanMove(Vector3Int position, Vector2Byte size, int entityId)
    {
        if (!_mapQuery.IsOnMap(position, size))
        {
            return false;
        }

        if (!_mapQuery.IsBlocking(entityId))
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
    /// </summary>
    private void SetRandomMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
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

            if (CanMove(positionToTest, size, entityId))
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

        _movementComponents.TryUpdate(entityId, FramesToWaitIfNoOptions, static (ref movementComponent, framesToWait) =>
        {
            movementComponent.FramesToWait = framesToWait;
        });
    }
}