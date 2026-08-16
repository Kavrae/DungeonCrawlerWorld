using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary> The in-memory map grid.</summary>
/// <remarks>
/// Three independent flat stores:
///     Blocking creature occupancy (one Blocking entity per (x,y,MapLayer) ) -- an O(1)
///         fast-path index doubling as the movement-collision "is this cell blocked, and by
///         whom" answer, kept even though every Blocking entity is also in the occupant store
///         below, precisely because that O(1) read matters on the movement hot path.
///     Occupant creature index (any number of entities per (x,y,MapLayer), Blocking entities
///         included) -- the "who is actually standing here" answer.
///     Terrain (the floor beneath UnderGround/Ground -- Flying has none).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class Map
{
    private static readonly int TerrainLayerCount = Enum.GetValues<TerrainLayer>().Length;

    /// <summary>The size of the map.</summary>
    public Vector3Int Size { get; }

    private readonly int[] _blockingEntityIds;
    private readonly int[] _terrainEntityIds;
    private readonly Dictionary<int, List<int>> _occupantEntityIdsByPosition = [];

    /// <summary>
    /// Lists emptied out by RemoveOccupantEntityId, kept here instead of left for the GC --
    /// a wandering population (e.g. Ghosts, now that they're genuinely exempt from collision)
    /// moves every cell it visits through exactly this empty-then-repopulate cycle, so without
    /// recycling, nearly every move both abandons one List (plus its backing array) and
    /// allocates a fresh one, entirely avoidable garbage at a population/move-rate where it
    /// adds up. AddOccupantEntityId draws from here before allocating; List.Clear() (called
    /// before a list re-enters the pool) keeps its backing array's Capacity, so a recycled list
    /// need not reallocate on its first reuse either.
    /// </summary>
    private readonly Stack<List<int>> _recycledEntityIdLists = new();

    private static readonly List<int> EmptyEntityIds = [];

    public Map(Vector3Int size)
    {
        Size = size;

        _blockingEntityIds = new int[size.Volume];
        Array.Fill(_blockingEntityIds, -1);

        _terrainEntityIds = new int[size.X * size.Y * TerrainLayerCount];
        Array.Fill(_terrainEntityIds, -1);
    }

    /// <summary>(0,0,0) is drawn to the top-left of the map window.</summary>
    public int GetBlockingEntityId(Vector3Int coordinates) => _blockingEntityIds[coordinates.FlatIndex(Size)];

    public void SetBlockingEntityId(Vector3Int position, int entityId) => _blockingEntityIds[position.FlatIndex(Size)] = entityId;

    /// <summary>Clears the cell only if it still records entityId. Returns whether it cleared anything.</summary>
    public bool ClearBlockingIfOccupiedBy(Vector3Int position, int entityId)
    {
        ref var occupantEntityId = ref _blockingEntityIds[position.FlatIndex(Size)];
        if (occupantEntityId != entityId)
        {
            return false;
        }

        occupantEntityId = -1;
        return true;
    }

    /// <summary>Every entity occupying position, Blocking or not -- empty if none. </summary>
    /// <remarks>Includes the (at most one) Blocking entity GetBlockingEntityId also answers for the same position -- this is the "who is actually standing here" answer, GetBlockingEntityId is the O(1) fast-path "is this cell blocked" one.</remarks>
    /// <param name="position">The position to query.</param>
    public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
        _occupantEntityIdsByPosition.TryGetValue(position.FlatIndex(Size), out var entityIds) ? entityIds : EmptyEntityIds;

    /// <summary>Adds an entity to the occupant index at the specified position.</summary>
    /// <param name="position">The position to add the entity to.</param>
    /// <param name="entityId">The ID of the entity to add.</param>
    public void AddOccupantEntityId(Vector3Int position, int entityId)
    {
        var key = position.FlatIndex(Size);
        if (!_occupantEntityIdsByPosition.TryGetValue(key, out var entityIds))
        {
            entityIds = _recycledEntityIdLists.Count > 0 ? _recycledEntityIdLists.Pop() : [];
            _occupantEntityIdsByPosition[key] = entityIds;
        }

        entityIds.Add(entityId);
    }

    /// <summary>Removes an entity from the occupant index at the specified position.</summary>
    /// <remarks>No-ops if entityId isn't actually recorded at position -- mirrors ClearIfOccupiedBy's own tolerance.</remarks>
    /// <param name="position">The position from which to remove the entity.</param>
    /// <param name="entityId">The ID of the entity to remove.</param>
    public void RemoveOccupantEntityId(Vector3Int position, int entityId)
    {
        var key = position.FlatIndex(Size);
        if (!_occupantEntityIdsByPosition.TryGetValue(key, out var entityIds))
        {
            return;
        }

        entityIds.Remove(entityId);
        if (entityIds.Count == 0)
        {
            _occupantEntityIdsByPosition.Remove(key);
            entityIds.Clear(); // No-op today (already empty), defends against Remove(entityId) above ever leaving stale entries once this is pooled rather than discarded.
            _recycledEntityIdLists.Push(entityIds);
        }
    }

    /// <summary>Gets the ID of the terrain entity at the specified position and terrain layer.</summary>
    /// <param name="x">The x-coordinate of the position.</param>
    /// <param name="y">The y-coordinate of the position.</param>
    /// <param name="terrainLayer">The terrain layer.</param>
    /// <returns>The ID of the terrain entity, or -1 if none exists.</returns>
    public int GetTerrainEntityId(int x, int y, TerrainLayer terrainLayer) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)];

    /// <summary>Sets the ID of the terrain entity at the specified position and terrain layer.</summary>
    /// <param name="x">The x-coordinate of the position.</param>
    /// <param name="y">The y-coordinate of the position.</param>
    /// <param name="terrainLayer">The terrain layer.</param>
    /// <param name="entityId">The ID of the entity to set.</param>
    public void SetTerrainEntityId(int x, int y, TerrainLayer terrainLayer, int entityId) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)] = entityId;

    /// <summary>Maps a MapLayer to its corresponding TerrainLayer, if any.</summary>
    /// <remarks> Ground and UnderGround each have a terrain floor beneath them; Flying is open air with none. </remarks>
    public static TerrainLayer? TerrainLayerFor(int mapLayer) => mapLayer switch
    {
        (int)MapLayer.UnderGround => TerrainLayer.UnderGround,
        (int)MapLayer.Ground => TerrainLayer.Ground,
        _ => null,
    };

    /// <summary>Calculates the index of the terrain entity at the specified position and terrain layer.</summary>
    /// <param name="x">The x-coordinate of the position.</param>
    /// <param name="y">The y-coordinate of the position.</param>
    /// <param name="terrainLayer">The terrain layer.</param>
    /// <returns>The index of the terrain entity.</returns>
    private int TerrainIndex(int x, int y, TerrainLayer terrainLayer) => new Vector3Int(x, y, (int)terrainLayer).FlatIndex(Size);
}