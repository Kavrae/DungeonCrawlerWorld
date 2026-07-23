using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>The in-memory game world: the map and bookkeeping for entities placed on it.</summary>
public sealed class World(Map map) : IMapQuery
{
    public Map Map { get; set; } = map ?? throw new ArgumentNullException(nameof(map));

    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    /// <summary>
    /// Set once ComponentManager exists (World itself is constructed before it, so these can't
    /// be constructor dependencies -- see GameLoop.cs). Null means "nobody has occupancy data
    /// yet," which IsBlocking treats as "everyone is Blocking," matching every pre-Occupancy
    /// test and blueprint unchanged.
    /// </summary>
    public MultiComponentPool<NonBlockingComponent>? NonBlockingComponents { get; set; }

    /// <inheritdoc cref="NonBlockingComponents"/>
    public MultiComponentPool<ForceBlockingComponent>? ForceBlockingComponents { get; set; }

    /// <summary>
    /// Moves entityId's map-index presence from transformComponent.Position to newPosition.
    /// No-ops (leaves the map's index untouched) if either footprint is off the map, or if
    /// the destination footprint is already occupied by a different Blocking entity -- both
    /// should be impossible given MovementSystem's own CanMove gate re-checking immediately
    /// before this is reached, but MoveEntity is a public method any future caller (including
    /// a mod's own Game-layer code, which can call it directly) can reach without going
    /// through that gate, so it defends itself rather than trusting the caller blindly. The
    /// free-space defense only applies to Blocking entities -- a Tiny/Phasing entity is exempt
    /// from map occupancy entirely and must never be refused here just because some other
    /// Blocking entity already occupies the destination. Map writes are skipped altogether for
    /// non-Blocking entities (see IsBlocking); transformComponent.Position still updates for
    /// everyone via the caller (WorldEventSync), since map-index presence and transform
    /// position are tracked independently.
    /// </summary>
    public void MoveEntity(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var extent = new Vector3Int(size.X, size.Y, 1); // A footprint never spans more than one MapLayer.
        var oldPosition = transformComponent.Position;
        var newCube = new CubeInt(newPosition, extent);

        var isBlocking = IsBlocking(entityId);

        if (!IsOnMap(newCube) || (isBlocking && !IsFootprintFreeFor(entityId, newCube)))
        {
            return;
        }

        if (!isBlocking)
        {
            return;
        }

        var oldZ = oldPosition.Z;
        var oldMaxX = oldPosition.X + size.X;
        var oldMaxY = oldPosition.Y + size.Y;
        for (var x = oldPosition.X; x < oldMaxX; x++)
        {
            for (var y = oldPosition.Y; y < oldMaxY; y++)
            {
                Map.ClearIfOccupiedBy(new Vector3Int(x, y, oldZ), entityId);
            }
        }

        var newZ = newPosition.Z;
        var newMaxX = newPosition.X + size.X;
        var newMaxY = newPosition.Y + size.Y;
        for (var x = newPosition.X; x < newMaxX; x++)
        {
            for (var y = newPosition.Y; y < newMaxY; y++)
            {
                Map.SetEntityId(new Vector3Int(x, y, newZ), entityId);
            }
        }
    }

    /// <summary>True if every cell in cube is either empty or already occupied by entityId.</summary>
    private bool IsFootprintFreeFor(int entityId, CubeInt cube)
    {
        var z = cube.Position.Z;
        var maxX = cube.Position.X + cube.Size.X;
        var maxY = cube.Position.Y + cube.Size.Y;
        for (var x = cube.Position.X; x < maxX; x++)
        {
            for (var y = cube.Position.Y; y < maxY; y++)
            {
                var occupyingEntityId = Map.GetEntityId(new Vector3Int(x, y, z));
                if (occupyingEntityId != -1 && occupyingEntityId != entityId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Fulfills IMapQuery.IsBlocking. ForceBlockingComponent wins if present (an effect
    /// forcing an otherwise-exempt entity solid); otherwise NonBlockingComponent exempts it;
    /// otherwise the default is Blocking. Both are Multi pools -- Has() means "at least one
    /// source is still active" -- so overlapping sources (two independent effects granting
    /// the same exemption) are handled correctly: one expiring doesn't affect the other.
    /// Absence of a pool (not wired up yet) is treated as "no sources," i.e. Blocking,
    /// matching every pre-Occupancy test and blueprint unchanged.
    /// </summary>
    public bool IsBlocking(int entityId)
    {
        if (ForceBlockingComponents is { } forceBlocking && forceBlocking.Has(entityId))
        {
            return true;
        }

        if (NonBlockingComponents is { } nonBlocking && nonBlocking.Has(entityId))
        {
            return false;
        }

        return true;
    }

    // Note: Position resets to (0,0,0) rather than a sentinel like Map's own -1 EntityId --
    // an inconsistency worth fixing once real despawn logic exercises this.
    public void RemoveEntityFromMap(int entityId, ref TransformComponent transformComponent)
    {
        if (IsOnMap(transformComponent.Position) && IsBlocking(entityId))
        {
            if (transformComponent.Size == TransformSize1)
            {
                Map.SetEntityId(transformComponent.Position, -1);
            }
            else
            {
                var z = transformComponent.Position.Z;
                for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
                {
                    for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                    {
                        Map.SetEntityId(new Vector3Int(x, y, z), -1);
                    }
                }
            }
        }

        transformComponent.Position = new Vector3Int();
    }

    public void PlaceEntityOnMap(int entityId, Vector3Int newPosition, ref TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        if (!IsOnMap(new CubeInt(newPosition, new Vector3Int(size.X, size.Y, 1)))) // A footprint never spans more than one MapLayer.
        {
            return;
        }

        if (IsBlocking(entityId))
        {
            if (transformComponent.Size == TransformSize1)
            {
                Map.SetEntityId(newPosition, entityId);
            }
            else
            {
                var z = newPosition.Z;
                for (var x = newPosition.X; x < newPosition.X + transformComponent.Size.X; x++)
                {
                    for (var y = newPosition.Y; y < newPosition.Y + transformComponent.Size.Y; y++)
                    {
                        Map.SetEntityId(new Vector3Int(x, y, z), entityId);
                    }
                }
            }
        }

        transformComponent.Position = newPosition;
    }

    /// <summary>
    /// Places a terrain entity (the floor beneath UnderGround/Ground -- never Flying, which
    /// has no floor). Terrain is always 1x1, never moves, and never blocks, so none of the
    /// footprint/occupancy logic above applies -- it writes directly to Map's separate terrain
    /// store instead of the creature-occupancy one.
    /// </summary>
    public void PlaceTerrainOnMap(int entityId, int x, int y, TerrainLayer terrainLayer)
    {
        if (!IsOnMap(new Vector3Int(x, y, 0)))
        {
            return;
        }

        Map.SetTerrainEntityId(x, y, terrainLayer, entityId);
    }

    public bool IsOnMap(Vector3Int coordinates) =>
        coordinates.X >= 0 && coordinates.Y >= 0 && coordinates.Z >= 0
        && coordinates.X < Map.Size.X && coordinates.Y < Map.Size.Y && coordinates.Z < Map.Size.Z;

    /// <summary>
    /// True only if the whole cube is on the map, so multi-tile entities never move
    /// partially off it. Size is an extent, not an inclusive far corner -- a cube occupies
    /// cells [Position, Position + Size), so the last actually-occupied cell is
    /// Position + Size - 1, not Position + Size. Checking Position + Size directly would
    /// reject a footprint sitting flush against the map's far edge (e.g. a 1x1x1 cube at the
    /// map's last valid row/column) even though every cell it occupies is on the map.
    /// </summary>
    public bool IsOnMap(CubeInt cube) => IsOnMap(cube.Position) && IsOnMap(cube.Position + cube.Size - new Vector3Int(1, 1, 1));

    /// <inheritdoc cref="IMapQuery"/>
    public Vector3Int MapSize => Map.Size;

    /// <inheritdoc cref="IMapQuery"/>
    public int GetEntityIdAt(Vector3Int position) => Map.GetEntityId(position);
}