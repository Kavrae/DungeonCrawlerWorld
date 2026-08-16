using Engine.Math;

namespace Game.World;

/// <summary>Position-based query interface for map information.</summary>
/// <remarks> This allows modded systems to have indirect access to map data without a direct reference to the map.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IMapQuery
{
    /// <summary>The current size of the map</summary>
    Vector3Int MapSize { get; }

    /// <summary>Checks if a position is on the map.</summary>
    /// <param name="position">The position to check.</param>
    /// <returns>True if the position is on the map, false otherwise.</returns>
    bool IsOnMap(Vector3Int position);

    /// <summary>Checks if a rectangle is on the map by position and size</summary>
    /// <remarks>Assumes the map is rectangular and only checks the top-left and bottom-right corners.</remarks>
    /// <param name="position">The position of the rectangle.</param>
    /// <param name="size">The size of the rectangle.</param>
    /// <returns>True if the rectangle is on the map, false otherwise.</returns>
    bool IsOnMap(Vector3Int position, Vector2Byte size)
    {
        if (!IsOnMap(position))
        {
            return false;
        }

        if (size.X == 1 && size.Y == 1)
        {
            return true;
        }

        return IsOnMap(new Vector3Int(position.X + size.X - 1, position.Y + size.Y - 1, position.Z));
    }

    /// <summary>The entity occupying position, or -1 if empty.</summary>
    int GetEntityIdAt(Vector3Int position);

    /// <summary>Gets the IDs of all non-blocking entities at a position.</summary>
    /// <param name="position">The position to check.</param>
    /// <returns>A list of entity IDs at the position.</returns>
    IReadOnlyList<int> GetNonBlockingEntityIdsAt(Vector3Int position) => [];

    /// <summary>Checks if an entity is blocking.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if the entity is blocking, false otherwise.</returns>
    bool IsBlocking(int entityId);

    /// <summary>Gets the ID of the terrain entity at a position.</summary>
    /// <param name="position">The position to check.</param>
    /// <returns>The ID of the terrain entity at the position, or -1 if none.</returns>
    int GetTerrainEntityIdAt(Vector3Int position);

    /// <summary>Gets the IDs of all entities within a bounding box.</summary>
    /// <param name="box">The bounding box to query.</param>
    /// <param name="entityIds">A span to fill with the entity IDs.</param>
    void GetEntityIdsInBox(CubeInt box, Span<int> entityIds);

    /// <summary>Whether any entity -- Blocking or non-Blocking -- currently occupies position.</summary>
    bool IsPositionOccupied(Vector3Int position) => GetEntityIdAt(position) != -1 || GetNonBlockingEntityIdsAt(position).Count > 0;
}