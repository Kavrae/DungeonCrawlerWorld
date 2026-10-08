using Engine.Math;

namespace Game.World;

/// <summary>Position-based query interface for map information.</summary>
/// <remarks> This allows modded systems to have indirect access to map data without a direct reference to the map.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IMapQuery
{
    /// <summary>The tiles the map covers.</summary>
    MapBounds Bounds { get; }

    /// <summary>Whether Bounds is a fixed, declared rectangle rather than one that follows whichever neighborhoods are loaded.</summary>
    /// <remarks>Anything that must answer the same for a position before and after the map's window moves can clip to Bounds only when this is true.</remarks>
    bool IsBounded => true;

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

    /// <summary>The exclusive Blocking entity occupying position, or -1 if none.</summary>
    /// <remarks>Never a non-Blocking entity, even if one occupies position -- callers wanting every occupant (Blocking or not) should use GetOccupantEntityIdsAt instead.</remarks>
    int GetEntityIdAt(Vector3Int position);

    /// <summary>Gets the IDs of every entity occupying a position, Blocking or not.</summary>
    /// <param name="position">The position to check.</param>
    /// <returns>A list of entity IDs at the position.</returns>
    IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) => [];

    /// <summary>The IDs of every entity occupying a position, Blocking or not, as a span -- empty off the map.</summary>
    /// <remarks>The span is valid until that cell's occupants change: a caller that can place, move, remove or destroy an entity while reading it copies the span first.</remarks>
    /// <param name="position">The position to check.</param>
    ReadOnlySpan<int> GetOccupantEntityIdSpanAt(Vector3Int position);

    /// <summary>Checks if an entity is blocking.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if the entity is blocking, false otherwise.</returns>
    bool IsBlocking(int entityId);

    /// <summary>The terrain under position's MapLayer -- empty off the map, on a layer with no floor, or where none was set.</summary>
    /// <remarks>Defaults to empty so a map query that has no terrain (most test doubles) needn't implement it.</remarks>
    Terrain.TerrainCell GetTerrainAt(Vector3Int position) => default;

    /// <summary>The structure standing on position's MapLayer -- empty off the map or where none was set.</summary>
    /// <remarks>Defaults to empty for the same reason as GetTerrainAt.</remarks>
    Terrain.TerrainCell GetStructureAt(Vector3Int position) => default;

    /// <summary>Whether position's structure or floor blocks movement. False off the map -- bounds are IsOnMap's answer.</summary>
    /// <remarks>Defaults to false for test doubles with no terrain.</remarks>
    bool IsCellBlocked(Vector3Int position) => false;

    /// <summary>Whether entityId passes through cells that block movement.</summary>
    /// <remarks>Defaults to false for test doubles with no phasing entities.</remarks>
    bool IsPhasing(int entityId) => false;

    /// <summary>Whether neighborhood (cellX, cellY) is loaded -- its cells are on the map and its entities exist.</summary>
    /// <remarks>Defaults to true for test doubles, which have no streaming.</remarks>
    bool IsNeighborhoodLoaded(int cellX, int cellY) => true;

    /// <summary>Gets the IDs of all entities within a bounding box.</summary>
    /// <param name="box">The bounding box to query.</param>
    /// <param name="entityIds">A span to fill with the entity IDs.</param>
    void GetEntityIdsInBox(CubeInt box, Span<int> entityIds);

    /// <summary>Whether any entity -- Blocking or non-Blocking -- currently occupies position.</summary>
    /// <remarks>GetOccupantEntityIdsAt's own contract already includes the Blocking occupant, if any (see its own doc comment) -- so checking GetEntityIdAt too would just repeat that same answer, not add coverage for a case GetOccupantEntityIdsAt could miss.</remarks>
    bool IsPositionOccupied(Vector3Int position) => GetOccupantEntityIdsAt(position).Count > 0;
}