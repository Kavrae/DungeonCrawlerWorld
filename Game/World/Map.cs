using Engine.Math;
using Game.Modules.Core.Components;
using Game.Terrain;
using System.Runtime.CompilerServices;

namespace Game.World;

/// <summary> The in-memory map grid.</summary>
/// <remarks>
/// Every per-cell store is split by neighborhood (see Neighborhoods and MapNeighborhood): a
/// position picks its neighborhood with a shift and its cell within it with a mask, so a whole
/// neighborhood's state is loaded and dropped in one step. Positions
/// stay world coordinates throughout, negative included. A bounded map covers a declared rectangle that
/// starts on a neighborhood boundary; an unbounded one (the sliding window) holds whichever whole
/// neighborhoods are loaded, found through a small grid of slots centred on the window, falling back
/// to a dictionary for anything outside it.
///
/// The stores:
///     Blocking creature occupancy (one Blocking entity per (x,y,MapLayer) ) -- an O(1)
///         fast-path index doubling as the movement-collision "is this cell blocked, and by
///         whom" answer, kept even though every Blocking entity is also in the occupant store
///         below, precisely because that O(1) read matters on the movement hot path.
///     Occupant creature index (any number of entities per (x,y,MapLayer), Blocking entities
///         included) -- the "who is actually standing here" answer. Split across a per-cell
///         index array and a dense list-of-lists, plus a per-column layer bitmask answering "is
///         any other layer of this column occupied" without a query per layer.
///     Terrain (the floor beneath UnderGround/Ground -- Flying has none): a TerrainCell per
///         (x, y, TerrainLayer), 3 bytes each, not an entity -- see TerrainDefinition.
///     Structures (walls standing on a MapLayer): a TerrainCell per (x, y, MapLayer), the same
///         definitions as terrain, independent of both the floor and the creatures.
///
/// The occupant index is a per-cell int array into a dense list-of-lists rather than a
/// Dictionary&lt;int, List&lt;int&gt;&gt;. The dictionary charged a full hash lookup for every query,
/// including the overwhelming majority that find nothing; MapWindow issues one per visible tile
/// per frame (measured at ~9ms of a 1000ms/sec budget), and MovementSystem,
/// StatusEffectAuraSystem, ActionEffectResolver and TestCombatBehaviorSystem all query it on hot
/// paths. The array makes the empty-cell case a single read: measured 63-74ns per dictionary
/// lookup against 29-32ns per array lookup over scattered whole-map lookups. It is a primitive
/// array rather than List&lt;int&gt;?[] because an array of millions of references is scanned in full
/// by every gen2 GC. Emptied list slots are recycled with their backing arrays, since a wandering
/// population takes cells through empty-then-repopulate constantly.
///
/// The per-column layer mask exists for MapWindow.DrawLayerBadges, which asks "is any layer
/// above/below this one occupied?" for every visible tile every frame -- measured at 17.4ms of a
/// 1000ms/sec budget as per-layer occupant lookups, replaced by one byte read and two bit tests.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class Map
{
    /// <summary>Maximum map depth GetOccupiedLayerMask's own byte-per-column encoding can represent -- one bit per MapLayer. MapLayer has three values today, so this is deliberately generous headroom rather than a live constraint; the constructor enforces it so a genuinely deeper map fails loudly here instead of silently losing its upper layers' badges.</summary>
    private const int MaximumMaskableLayers = 8;

    private const int NoOccupantListIndex = -1;

    private const int CellMask = Neighborhoods.SizeTiles - 1;

    private static readonly List<int> EmptyEntityIds = [];

    /// <summary>The tiles the map covers: a bounded map's declared rectangle, or the smallest rectangle holding every loaded neighborhood of an unbounded one.</summary>
    /// <remarks>A rectangle, so it can hold unloaded neighborhoods too; Contains is the authoritative on-the-map test.</remarks>
    public MapBounds Bounds { get; private set; }

    /// <summary>The declared rectangle of a bounded map; null for an unbounded one, which loads whole neighborhoods anywhere.</summary>
    private readonly MapBounds? _declaredBounds;

    private readonly int _depth;

    private readonly Dictionary<(int CellX, int CellY), MapNeighborhood> _loadedNeighborhoods = [];

    /// <summary>A square of neighborhood slots mirroring _loadedNeighborhoods around one origin, so the neighborhoods queries hit most are an array read rather than a dictionary lookup.</summary>
    private readonly MapNeighborhood?[] _lookupGrid;

    private readonly int _lookupGridSide;

    private int _lookupGridOriginCellX;

    private int _lookupGridOriginCellY;

    /// <summary>A map covering size's extent from (0, 0).</summary>
    public Map(Vector3Int size)
        : this(MapBounds.FromSize(size))
    {
    }

    /// <summary>A bounded map covering bounds, every neighborhood loaded.</summary>
    /// <remarks>Bounds must start on a neighborhood boundary; a neighborhood the far edges cut off is only as large as its part inside them.</remarks>
    public Map(MapBounds bounds)
        : this(bounds.Depth, bounds, System.Math.Max(NeighborhoodsToCover(bounds.Width), NeighborhoodsToCover(bounds.Height)), Neighborhoods.CellOf(bounds.MinX), Neighborhoods.CellOf(bounds.MinY))
    {
        if ((bounds.MinX & CellMask) != 0 || (bounds.MinY & CellMask) != 0)
        {
            throw new ArgumentException($"Bounds must start on a neighborhood boundary, not ({bounds.MinX}, {bounds.MinY}).", nameof(bounds));
        }

        for (var cellY = Neighborhoods.CellOf(bounds.MinY); cellY < Neighborhoods.CellOf(bounds.MinY) + NeighborhoodsToCover(bounds.Height); cellY++)
        {
            for (var cellX = Neighborhoods.CellOf(bounds.MinX); cellX < Neighborhoods.CellOf(bounds.MinX) + NeighborhoodsToCover(bounds.Width); cellX++)
            {
                LoadNeighborhood(cellX, cellY);
            }
        }
    }

    private Map(int depth, MapBounds? declaredBounds, int lookupGridSide, int lookupGridOriginCellX, int lookupGridOriginCellY)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, MaximumMaskableLayers, nameof(depth));

        _depth = depth;
        _declaredBounds = declaredBounds;
        _lookupGridSide = lookupGridSide;
        _lookupGrid = new MapNeighborhood?[lookupGridSide * lookupGridSide];
        _lookupGridOriginCellX = lookupGridOriginCellX;
        _lookupGridOriginCellY = lookupGridOriginCellY;
        Bounds = declaredBounds ?? new MapBounds(0, 0, 0, 0, depth);
    }

    /// <summary>An unbounded map with no neighborhoods loaded: LoadNeighborhood takes whole neighborhoods at any coordinate, and Bounds follows what is loaded.</summary>
    /// <param name="lookupGridSide">How many neighborhoods square the fast lookup covers around CenterLookupOn's cell; neighborhoods outside it still work, through a dictionary.</param>
    public static Map Unbounded(int depth, int lookupGridSide = 5) => new(depth, declaredBounds: null, lookupGridSide, -lookupGridSide / 2, -lookupGridSide / 2);

    private static int NeighborhoodsToCover(int tiles) => (tiles + Neighborhoods.SizeTiles - 1) >> Neighborhoods.SizeShift;

    /// <summary>Whether position is in a loaded neighborhood, inside its part of a bounded map, on one of the map's layers.</summary>
    public bool Contains(Vector3Int position) =>
        (uint)position.Z < (uint)_depth &&
        NeighborhoodAt(position.X, position.Y) is { } neighborhood &&
        (position.X & CellMask) < neighborhood.Width &&
        (position.Y & CellMask) < neighborhood.Height;

    /// <summary>Whether the map covers a fixed, declared rectangle rather than sliding with whatever is loaded.</summary>
    public bool IsBounded => _declaredBounds is not null;

    /// <summary>Whether neighborhood (cellX, cellY) could be loaded: anywhere on an unbounded map, inside the declared rectangle of a bounded one.</summary>
    public bool CanHold(int cellX, int cellY) =>
        _declaredBounds is not { } declared ||
        (Neighborhoods.OriginOf(cellX) >= declared.MinX && Neighborhoods.OriginOf(cellX) < declared.MaxX && Neighborhoods.OriginOf(cellY) >= declared.MinY && Neighborhoods.OriginOf(cellY) < declared.MaxY);

    /// <summary>Whether neighborhood (cellX, cellY) is loaded.</summary>
    public bool IsNeighborhoodLoaded(int cellX, int cellY) => _loadedNeighborhoods.ContainsKey((cellX, cellY));

    /// <summary>Every loaded neighborhood's coordinate, in no particular order.</summary>
    public IEnumerable<(int CellX, int CellY)> LoadedNeighborhoods => _loadedNeighborhoods.Keys;

    /// <summary>Allocates neighborhood (cellX, cellY)'s stores, empty: no terrain, structures or occupants.</summary>
    /// <remarks>On a bounded map, only a neighborhood inside the declared rectangle, and only as large as its part inside it.</remarks>
    public void LoadNeighborhood(int cellX, int cellY)
    {
        if (_loadedNeighborhoods.ContainsKey((cellX, cellY)))
        {
            throw new InvalidOperationException($"Neighborhood ({cellX}, {cellY}) is already loaded.");
        }

        LoadNeighborhood(CreateLayout(cellX, cellY));
    }

    /// <summary>New, empty stores for neighborhood (cellX, cellY), not yet part of the map -- to be filled, on any thread, and handed to LoadNeighborhood.</summary>
    /// <remarks>Reads only what never changes about the map (its depth and declared rectangle), so it is safe off the main thread.</remarks>
    public NeighborhoodLayout CreateLayout(int cellX, int cellY)
    {
        var neighborhoodWidth = Neighborhoods.SizeTiles;
        var neighborhoodHeight = Neighborhoods.SizeTiles;
        if (_declaredBounds is { } declaredBounds)
        {
            neighborhoodWidth = System.Math.Min(neighborhoodWidth, declaredBounds.MaxX - Neighborhoods.OriginOf(cellX));
            neighborhoodHeight = System.Math.Min(neighborhoodHeight, declaredBounds.MaxY - Neighborhoods.OriginOf(cellY));
            if (!CanHold(cellX, cellY))
            {
                throw new ArgumentOutOfRangeException(nameof(cellX), $"Neighborhood ({cellX}, {cellY}) is outside the map's bounds.");
            }
        }

        return new NeighborhoodLayout(cellX, cellY, new MapNeighborhood(neighborhoodWidth, neighborhoodHeight, _depth));
    }

    /// <summary>Loads layout's neighborhood with layout's terrain and structures.</summary>
    /// <remarks>
    /// An unloaded neighborhood takes layout's stores as they are. An already loaded one -- every
    /// neighborhood of a bounded map, and the startup window -- copies layout's terrain and structures in
    /// and keeps its occupants, which may include a neighbor's multi-tile creature reaching across the
    /// border. Either way layout must not be used afterwards.
    /// </remarks>
    public void LoadNeighborhood(NeighborhoodLayout layout)
    {
        var neighborhoodCoordinate = (layout.CellX, layout.CellY);
        var layoutCellStores = layout.NeighborhoodCellStores;
        if (_loadedNeighborhoods.TryGetValue(neighborhoodCoordinate, out var loadedCellStores))
        {
            if (layoutCellStores.Width != loadedCellStores.Width || layoutCellStores.Height != loadedCellStores.Height || layoutCellStores.StructureTypeIds.Length != loadedCellStores.StructureTypeIds.Length)
            {
                throw new ArgumentException($"The layout for neighborhood {neighborhoodCoordinate} doesn't match its stores.", nameof(layout));
            }

            layoutCellStores.TerrainTypeIds.CopyTo(loadedCellStores.TerrainTypeIds, 0);
            layoutCellStores.TerrainVariants.CopyTo(loadedCellStores.TerrainVariants, 0);
            layoutCellStores.StructureTypeIds.CopyTo(loadedCellStores.StructureTypeIds, 0);
            layoutCellStores.StructureVariants.CopyTo(loadedCellStores.StructureVariants, 0);
            return;
        }

        _loadedNeighborhoods.Add(neighborhoodCoordinate, layoutCellStores);
        SetLookupSlot(layout.CellX, layout.CellY, layoutCellStores);
        RecomputeBounds();
    }

    /// <summary>Drops neighborhood (cellX, cellY)'s stores, so its positions are no longer on the map.</summary>
    /// <remarks>Everything placed there must already have been removed; nothing it held is kept.</remarks>
    public void UnloadNeighborhood(int cellX, int cellY)
    {
        if (!_loadedNeighborhoods.Remove((cellX, cellY)))
        {
            throw new ArgumentOutOfRangeException(nameof(cellX), $"Neighborhood ({cellX}, {cellY}) isn't loaded.");
        }

        SetLookupSlot(cellX, cellY, null);
        RecomputeBounds();
    }

    /// <summary>Moves the fast lookup to the square of neighborhoods centred on (cellX, cellY) -- where the window is. What is loaded doesn't change.</summary>
    public void CenterLookupOn(int cellX, int cellY)
    {
        _lookupGridOriginCellX = cellX - _lookupGridSide / 2;
        _lookupGridOriginCellY = cellY - _lookupGridSide / 2;
        Array.Clear(_lookupGrid);
        foreach (var ((loadedCellX, loadedCellY), neighborhood) in _loadedNeighborhoods)
        {
            SetLookupSlot(loadedCellX, loadedCellY, neighborhood);
        }
    }

    private void SetLookupSlot(int cellX, int cellY, MapNeighborhood? neighborhood)
    {
        var column = cellX - _lookupGridOriginCellX;
        var row = cellY - _lookupGridOriginCellY;
        if ((uint)column < (uint)_lookupGridSide && (uint)row < (uint)_lookupGridSide)
        {
            _lookupGrid[column + row * _lookupGridSide] = neighborhood;
        }
    }

    /// <summary>A bounded map keeps its declared rectangle; an unbounded one takes the smallest rectangle around what is loaded.</summary>
    private void RecomputeBounds()
    {
        if (_declaredBounds is not null)
        {
            return;
        }

        if (_loadedNeighborhoods.Count == 0)
        {
            Bounds = new MapBounds(0, 0, 0, 0, _depth);
            return;
        }

        int minCellX = int.MaxValue, minCellY = int.MaxValue, maxCellX = int.MinValue, maxCellY = int.MinValue;
        foreach (var (cellX, cellY) in _loadedNeighborhoods.Keys)
        {
            minCellX = System.Math.Min(minCellX, cellX);
            minCellY = System.Math.Min(minCellY, cellY);
            maxCellX = System.Math.Max(maxCellX, cellX);
            maxCellY = System.Math.Max(maxCellY, cellY);
        }

        Bounds = new MapBounds(Neighborhoods.OriginOf(minCellX), Neighborhoods.OriginOf(minCellY), Neighborhoods.OriginOf(maxCellX + 1), Neighborhoods.OriginOf(maxCellY + 1), _depth);
    }

    /// <summary>The neighborhood holding (x, y), or null where it is unloaded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MapNeighborhood? NeighborhoodAt(int x, int y)
    {
        var column = Neighborhoods.CellOf(x) - _lookupGridOriginCellX;
        var row = Neighborhoods.CellOf(y) - _lookupGridOriginCellY;
        return (uint)column < (uint)_lookupGridSide && (uint)row < (uint)_lookupGridSide
            ? _lookupGrid[column + row * _lookupGridSide]
            : _loadedNeighborhoods.GetValueOrDefault((Neighborhoods.CellOf(x), Neighborhoods.CellOf(y)));
    }

    /// <summary>The loaded neighborhood holding (x, y), for writers: callers guarantee the position is on the map.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MapNeighborhood LoadedNeighborhoodAt(int x, int y) =>
        NeighborhoodAt(x, y) ?? throw new InvalidOperationException($"({x}, {y}) is in an unloaded neighborhood.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ColumnIndex(MapNeighborhood neighborhood, int x, int y) => (x & CellMask) + (y & CellMask) * neighborhood.Width;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CellIndex(MapNeighborhood neighborhood, Vector3Int position) =>
        ColumnIndex(neighborhood, position.X, position.Y) + position.Z * neighborhood.PlaneSize;

    /// <summary>The Blocking occupant at coordinates, or -1, including where its neighborhood is unloaded..</summary>
    public int GetBlockingEntityId(Vector3Int coordinates)
    {
        var neighborhood = NeighborhoodAt(coordinates.X, coordinates.Y);
        if (neighborhood is null)
        {
            return -1;
        }

        return neighborhood.BlockingEntityIds[CellIndex(neighborhood, coordinates)];
    }

    public void SetBlockingEntityId(Vector3Int position, int entityId)
    {
        var neighborhood = LoadedNeighborhoodAt(position.X, position.Y);
        neighborhood.BlockingEntityIds[CellIndex(neighborhood, position)] = entityId;
    }

    /// <summary>Clears the cell only if it still records entityId. Returns whether it cleared anything; an unloaded cell records nothing.</summary>
    public bool ClearBlockingIfOccupiedBy(Vector3Int position, int entityId)
    {
        var neighborhood = NeighborhoodAt(position.X, position.Y);
        if (neighborhood is null)
        {
            return false;
        }

        ref var occupantEntityId = ref neighborhood.BlockingEntityIds[CellIndex(neighborhood, position)];
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
        TryGetOccupantList(position, out var entityIds) ? entityIds : EmptyEntityIds;

    /// <summary>
    /// The same occupants GetOccupantEntityIdsAt answers with, as a span rather than an
    /// interface -- for the per-frame draw path, which reads this for every visible tile.
    /// </summary>
    /// <remarks>
    /// foreach over an IReadOnlyList&lt;int&gt; binds to IEnumerable&lt;int&gt;.GetEnumerator and so boxes
    /// List&lt;int&gt;'s struct enumerator on the heap -- measured at 40 bytes per call on a non-empty
    /// list (an empty one is free, the BCL hands back a shared instance). MapWindow read this
    /// three times per occupied tile per frame, so at a few thousand visible tiles that was
    /// thousands of short-lived allocations a frame, against the heap this game's entity-indexed
    /// component arrays already live on. Indexing through the interface avoids the allocation but
    /// still pays an interface dispatch per element; a span avoids both.
    ///
    /// The span is only valid until the next mutation of this cell's own list (Add/Remove
    /// OccupantEntityId), which is the same constraint any direct List access would carry --
    /// callers here are read-only per-frame draw passes, not anything that moves entities.
    /// </remarks>
    /// <param name="position">The position to query.</param>
    public ReadOnlySpan<int> GetOccupantEntityIdSpanAt(Vector3Int position) =>
        TryGetOccupantList(position, out var entityIds)
            ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(entityIds)
            : ReadOnlySpan<int>.Empty;

    /// <summary>True if any entity occupies position -- the same answer as GetOccupantEntityIdsAt(position).Count &gt; 0, without going through the list at all.</summary>
    /// <param name="position">The position to test.</param>
    public bool HasOccupantAt(Vector3Int position) => TryGetOccupantList(position, out _);

    /// <summary>Resolves position to its occupant list, or false when the cell is empty or off the map.</summary>
    /// <remarks>
    /// The off-map check is deliberate rather than incidental: callers rely on an off-map position
    /// answering as empty (ActionEffectResolver and TestCombatBehaviorSystem both walk resolved
    /// target tiles without re-checking bounds first). Each coordinate is checked on its own, so an
    /// out-of-range coordinate never aliases into some other valid cell.
    /// </remarks>
    private bool TryGetOccupantList(Vector3Int position, out List<int> entityIds)
    {
        if (!Contains(position))
        {
            entityIds = EmptyEntityIds;
            return false;
        }

        var neighborhood = NeighborhoodAt(position.X, position.Y);
        if (neighborhood is null)
        {
            entityIds = EmptyEntityIds;
            return false;
        }

        var listIndex = neighborhood.OccupantListIndexByCell[CellIndex(neighborhood, position)];
        if (listIndex == NoOccupantListIndex)
        {
            entityIds = EmptyEntityIds;
            return false;
        }

        entityIds = neighborhood.OccupantLists[listIndex];
        return true;
    }

    /// <summary>Adds an entity to the occupant index at the specified position.</summary>
    /// <param name="position">The position to add the entity to.</param>
    /// <param name="entityId">The ID of the entity to add.</param>
    public void AddOccupantEntityId(Vector3Int position, int entityId)
    {
        var neighborhood = LoadedNeighborhoodAt(position.X, position.Y);
        var cellIndex = CellIndex(neighborhood, position);
        var listIndex = neighborhood.OccupantListIndexByCell[cellIndex];

        if (listIndex == NoOccupantListIndex)
        {
            if (neighborhood.FreeOccupantListIndices.Count > 0)
            {
                listIndex = neighborhood.FreeOccupantListIndices.Pop();
            }
            else
            {
                listIndex = neighborhood.OccupantLists.Count;
                neighborhood.OccupantLists.Add([]);
            }

            neighborhood.OccupantListIndexByCell[cellIndex] = listIndex;
        }

        neighborhood.OccupantLists[listIndex].Add(entityId);
        neighborhood.OccupiedLayerMaskByColumn[ColumnIndex(neighborhood, position.X, position.Y)] |= LayerBit(position.Z);
    }

    /// <summary>Removes an entity from the occupant index at the specified position.</summary>
    /// <remarks>No-ops if entityId isn't actually recorded at position, including where the neighborhood is unloaded -- mirrors ClearIfOccupiedBy's own tolerance.</remarks>
    /// <param name="position">The position from which to remove the entity.</param>
    /// <param name="entityId">The ID of the entity to remove.</param>
    public void RemoveOccupantEntityId(Vector3Int position, int entityId)
    {
        var neighborhood = NeighborhoodAt(position.X, position.Y);
        if (neighborhood is null)
        {
            return;
        }

        var cellIndex = CellIndex(neighborhood, position);
        var listIndex = neighborhood.OccupantListIndexByCell[cellIndex];
        if (listIndex == NoOccupantListIndex)
        {
            return;
        }

        var entityIds = neighborhood.OccupantLists[listIndex];
        entityIds.Remove(entityId);
        if (entityIds.Count == 0)
        {
            // The list stays attached to its slot rather than being handed back separately -- it is
            // already empty, and keeping it means the next cell to claim this slot inherits its
            // backing array and Capacity for free.
            neighborhood.OccupantListIndexByCell[cellIndex] = NoOccupantListIndex;
            neighborhood.FreeOccupantListIndices.Push(listIndex);

            // Cleared only when the cell empties completely -- the bit means "this cell has at
            // least one occupant," so it must survive a removal that leaves others behind.
            neighborhood.OccupiedLayerMaskByColumn[ColumnIndex(neighborhood, position.X, position.Y)] &= (byte)~LayerBit(position.Z);
        }
    }

    /// <summary>
    /// A bitmask of which MapLayers currently hold at least one occupant at (x, y) -- bit N is
    /// layer N. The O(1) answer to "is any other layer of this column occupied," without a
    /// per-layer occupant lookup; see this class's remarks for why this exists.
    /// </summary>
    /// <param name="x">The x-coordinate of the column.</param>
    /// <param name="y">The y-coordinate of the column.</param>
    public byte GetOccupiedLayerMask(int x, int y)
    {
        var neighborhood = NeighborhoodAt(x, y);
        if (neighborhood is null)
        {
            return 0;
        }

        return neighborhood.OccupiedLayerMaskByColumn[ColumnIndex(neighborhood, x, y)];
    }

    private static byte LayerBit(int layer) => (byte)(1 << layer);

    /// <summary>The terrain at (x, y) on terrainLayer -- an empty cell where nothing was set or the neighborhood is unloaded.</summary>
    public TerrainCell GetTerrain(int x, int y, TerrainLayer terrainLayer)
    {
        var neighborhood = NeighborhoodAt(x, y);
        if (neighborhood is null)
        {
            return default;
        }

        var index = TerrainIndex(neighborhood, x, y, terrainLayer);
        return new TerrainCell(neighborhood.TerrainTypeIds[index], neighborhood.TerrainVariants[index]);
    }

    /// <summary>Writes the terrain at (x, y) on terrainLayer. Map stores only; World.SetTerrain is what tells anyone.</summary>
    public void SetTerrain(int x, int y, TerrainLayer terrainLayer, TerrainCell cell)
    {
        var neighborhood = LoadedNeighborhoodAt(x, y);
        var index = TerrainIndex(neighborhood, x, y, terrainLayer);
        neighborhood.TerrainTypeIds[index] = cell.TypeId;
        neighborhood.TerrainVariants[index] = cell.Variant;
    }

    /// <summary>The structure at position -- an empty cell where nothing was set or the neighborhood is unloaded.</summary>
    public TerrainCell GetStructure(Vector3Int position)
    {
        var neighborhood = NeighborhoodAt(position.X, position.Y);
        if (neighborhood is null)
        {
            return default;
        }

        var index = CellIndex(neighborhood, position);
        return new TerrainCell(neighborhood.StructureTypeIds[index], neighborhood.StructureVariants[index]);
    }

    /// <summary>The structure's type id at position, without its variant, for the blocking check.</summary>
    public ushort GetStructureTypeId(Vector3Int position)
    {
        var neighborhood = NeighborhoodAt(position.X, position.Y);
        if (neighborhood is null)
        {
            return 0;
        }

        return neighborhood.StructureTypeIds[CellIndex(neighborhood, position)];
    }

    /// <summary>Writes the structure at position. Map stores only; World.SetStructure is what tells anyone.</summary>
    public void SetStructure(Vector3Int position, TerrainCell cell)
    {
        var neighborhood = LoadedNeighborhoodAt(position.X, position.Y);
        var index = CellIndex(neighborhood, position);
        neighborhood.StructureTypeIds[index] = cell.TypeId;
        neighborhood.StructureVariants[index] = cell.Variant;
    }

    /// <summary>Maps a MapLayer to its corresponding TerrainLayer, if any.</summary>
    /// <remarks> Ground and UnderGround each have a terrain floor beneath them; Flying is open air with none. </remarks>
    public static TerrainLayer? TerrainLayerFor(int mapLayer) => mapLayer switch
    {
        (int)MapLayer.UnderGround => TerrainLayer.UnderGround,
        (int)MapLayer.Ground => TerrainLayer.Ground,
        _ => null,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TerrainIndex(MapNeighborhood neighborhood, int x, int y, TerrainLayer terrainLayer) =>
        ColumnIndex(neighborhood, x, y) + (int)terrainLayer * neighborhood.PlaneSize;
}
