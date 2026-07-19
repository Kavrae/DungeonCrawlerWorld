using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.World;

/// <summary>The in-memory game world: the map and bookkeeping for entities placed on it.</summary>
public sealed class World : IMapQuery
{
    private static readonly Vector3Byte TransformSize1 = new(1, 1, 1);

    public Map Map { get; set; }

    /// <summary>2D coordinates of the currently selected map node, if any -- includes all Z layers at that XY.</summary>
    public Point? SelectedMapNodePosition { get; set; }

    public World(Map map)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <summary>
    /// Moves entityId's map-index presence from transformComponent.Position to newPosition.
    /// No-ops (leaves the map's index untouched) if either footprint is off the map, or if
    /// the destination footprint is already occupied by a different entity -- both should be
    /// impossible given MovementSystem's own CanMove gate re-checking immediately before
    /// this is reached, but MoveEntity is a public method any future caller (including a
    /// mod's own Game-layer code, which can call it directly) can reach without going through
    /// that gate, so it defends itself rather than trusting the caller blindly. Clearing the
    /// origin footprint only clears cells that still record entityId, so a caller passing a
    /// stale or wrong old position can't corrupt a different entity's occupancy record.
    /// </summary>
    public void MoveEntity(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var extent = new Vector3Int(size.X, size.Y, size.Z);
        var oldPosition = transformComponent.Position;
        var oldCube = new CubeInt(oldPosition, extent);
        var newCube = new CubeInt(newPosition, extent);

        if (!IsOnMap(oldCube) || !IsOnMap(newCube) || !IsFootprintFreeFor(entityId, newCube))
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
                ref var node = ref Map.MapNodes[x, y, oldZ];
                if (node.EntityId == entityId)
                {
                    node.EntityId = -1;
                }
            }
        }

        var newZ = newPosition.Z;
        var newMaxX = newPosition.X + size.X;
        var newMaxY = newPosition.Y + size.Y;
        for (var x = newPosition.X; x < newMaxX; x++)
        {
            for (var y = newPosition.Y; y < newMaxY; y++)
            {
                Map.MapNodes[x, y, newZ].EntityId = entityId;
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
                var occupyingEntityId = Map.MapNodes[x, y, z].EntityId;
                if (occupyingEntityId != -1 && occupyingEntityId != entityId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Note: Position resets to (0,0,0) rather than a sentinel like MapNode's EntityId=-1 --
    // an inconsistency worth fixing once real despawn logic exercises this.
    public void RemoveEntityFromMap(int entityId, ref TransformComponent transformComponent)
    {
        if (IsOnMap(transformComponent.Position))
        {
            if (transformComponent.Size == TransformSize1)
            {
                Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z].EntityId = -1;
            }
            else
            {
                var z = transformComponent.Position.Z;
                for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
                {
                    for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                    {
                        Map.MapNodes[x, y, z].EntityId = -1;
                    }
                }
            }
        }

        transformComponent.Position = new Vector3Int();
    }

    public void PlaceEntityOnMap(int entityId, Vector3Int newPosition, ref TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        if (!IsOnMap(new CubeInt(newPosition, new Vector3Int(size.X, size.Y, size.Z))))
        {
            return;
        }

        if (transformComponent.Size == TransformSize1)
        {
            Map.MapNodes[newPosition.X, newPosition.Y, newPosition.Z].EntityId = entityId;
        }
        else
        {
            var z = newPosition.Z;
            for (var x = newPosition.X; x < newPosition.X + transformComponent.Size.X; x++)
            {
                for (var y = newPosition.Y; y < newPosition.Y + transformComponent.Size.Y; y++)
                {
                    Map.MapNodes[x, y, z].EntityId = entityId;
                }
            }
        }

        transformComponent.Position = newPosition;
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
    public int GetEntityIdAt(Vector3Int position) => Map.GetMapNode(position).EntityId;
}
