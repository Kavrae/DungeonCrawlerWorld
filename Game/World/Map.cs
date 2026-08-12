using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// The in-memory map grid. Two independent flat stores: creature occupancy (one Blocking
/// entity per (x,y,MapLayer) cell -- see World.IsBlocking for why Tiny/Phasing entities never
/// occupy this) and terrain (the floor beneath UnderGround/Ground -- Flying has none). Kept
/// separate so a wall and the floor it stands on don't compete for the same slot. A third
/// store, _nonBlockingEntityIdsByPosition, indexes every non-Blocking entity by position --
/// see its own doc comment.
/// </summary>
public sealed class Map
{
    private const int TerrainLayerCount = 2;

    public Vector3Int Size { get; }

    private readonly int[] _occupantEntityIds;
    private readonly int[] _terrainEntityIds;

    /// <summary>
    /// Position-keyed index of non-Blocking entities -- unlike _occupantEntityIds (one Blocking
    /// entity per cell, a flat array), any number of non-Blocking entities can share a cell
    /// (stacking, or overlapping a Blocking entity), and the population is expected to be a
    /// small fraction of the map, so a sparse Dictionary is used instead of a second flat array.
    /// Kept incrementally in sync by World's placement/move/removal methods, mirroring how
    /// _occupantEntityIds already is for Blocking entities -- see TODO.md's now-resolved
    /// "Occupancy rendering/selection scans assume a small Tiny/Phasing population" entry for
    /// why a real index replaced the full-pool-scan this used to require.
    /// </summary>
    private readonly Dictionary<int, List<int>> _nonBlockingEntityIdsByPosition = [];

    /// <summary>
    /// Lists emptied out by RemoveNonBlockingEntityId, kept here instead of left for the GC --
    /// a wandering non-Blocking population (e.g. Ghosts, now that they're genuinely exempt from
    /// collision) moves every cell it visits through exactly this empty-then-repopulate cycle,
    /// so without recycling, nearly every move both abandons one List (plus its backing array)
    /// and allocates a fresh one, entirely avoidable garbage at a population/move-rate where it
    /// adds up. AddNonBlockingEntityId draws from here before allocating; List.Clear() (called
    /// before a list re-enters the pool) keeps its backing array's Capacity, so a recycled list
    /// need not reallocate on its first reuse either.
    /// </summary>
    private readonly Stack<List<int>> _recycledEntityIdLists = new();

    private static readonly List<int> EmptyEntityIds = [];

    public Map(Vector3Int size)
    {
        Size = size;

        _occupantEntityIds = new int[size.Volume];
        Array.Fill(_occupantEntityIds, -1);

        _terrainEntityIds = new int[size.X * size.Y * TerrainLayerCount];
        Array.Fill(_terrainEntityIds, -1);
    }

    /// <summary>(0,0,0) is drawn to the top-left of the map window.</summary>
    public int GetEntityId(Vector3Int coordinates) => _occupantEntityIds[coordinates.FlatIndex(Size)];

    public void SetEntityId(Vector3Int position, int entityId) => _occupantEntityIds[position.FlatIndex(Size)] = entityId;

    /// <summary>Clears the cell only if it still records entityId. Returns whether it cleared anything.</summary>
    public bool ClearIfOccupiedBy(Vector3Int position, int entityId)
    {
        ref var occupantEntityId = ref _occupantEntityIds[position.FlatIndex(Size)];
        if (occupantEntityId != entityId)
        {
            return false;
        }

        occupantEntityId = -1;
        return true;
    }

    /// <summary>Every non-Blocking entity occupying position -- empty if none. Does NOT include the (at most one) Blocking entity GetEntityId already answers for the same position; callers that need everyone at a tile combine both (see ActionEffectResolver.Apply).</summary>
    public IReadOnlyList<int> GetNonBlockingEntityIdsAt(Vector3Int position) =>
        _nonBlockingEntityIdsByPosition.TryGetValue(position.FlatIndex(Size), out var entityIds) ? entityIds : EmptyEntityIds;

    public void AddNonBlockingEntityId(Vector3Int position, int entityId)
    {
        var key = position.FlatIndex(Size);
        if (!_nonBlockingEntityIdsByPosition.TryGetValue(key, out var entityIds))
        {
            entityIds = _recycledEntityIdLists.Count > 0 ? _recycledEntityIdLists.Pop() : [];
            _nonBlockingEntityIdsByPosition[key] = entityIds;
        }

        entityIds.Add(entityId);
    }

    /// <summary>No-ops if entityId isn't actually recorded at position -- mirrors ClearIfOccupiedBy's own tolerance.</summary>
    public void RemoveNonBlockingEntityId(Vector3Int position, int entityId)
    {
        var key = position.FlatIndex(Size);
        if (!_nonBlockingEntityIdsByPosition.TryGetValue(key, out var entityIds))
        {
            return;
        }

        entityIds.Remove(entityId);
        if (entityIds.Count == 0)
        {
            _nonBlockingEntityIdsByPosition.Remove(key);
            entityIds.Clear(); // No-op today (already empty), defends against Remove(entityId) above ever leaving stale entries once this is pooled rather than discarded.
            _recycledEntityIdLists.Push(entityIds);
        }
    }

    public int GetTerrainEntityId(int x, int y, TerrainLayer terrainLayer) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)];

    public void SetTerrainEntityId(int x, int y, TerrainLayer terrainLayer, int entityId) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)] = entityId;

    /// <summary>
    /// Ground and UnderGround each have a terrain floor beneath them; Flying is open air with
    /// none. The single source of truth for this mapping -- MapWindow (what to render) and
    /// SelectionWindowContent (what to inspect) both call this rather than each keeping their
    /// own copy.
    /// </summary>
    public static TerrainLayer? TerrainLayerFor(int mapLayer) => mapLayer switch
    {
        (int)MapLayer.UnderGround => TerrainLayer.UnderGround,
        (int)MapLayer.Ground => TerrainLayer.Ground,
        _ => null,
    };

    private int TerrainIndex(int x, int y, TerrainLayer terrainLayer) => new Vector3Int(x, y, (int)terrainLayer).FlatIndex(Size);
}