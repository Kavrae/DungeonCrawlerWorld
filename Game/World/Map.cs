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
///         included) -- the "who is actually standing here" answer. Split across a per-cell
///         index array and a dense list-of-lists (see _occupantListIndexByCell), plus a
///         per-column layer bitmask (see _occupiedLayerMaskByColumn) answering "is any other
///         layer of this column occupied" without a query per layer.
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

    /// <summary>Sentinel in _occupantListIndexByCell for "this cell holds nobody."</summary>
    private const int NoOccupantList = -1;

    /// <summary>
    /// Per-cell index into _occupantLists, or NoOccupantList when the cell is empty -- the first
    /// hop of the occupant lookup, and the one that runs for every cell whether occupied or not.
    /// </summary>
    /// <remarks>
    /// This replaced a Dictionary&lt;int, List&lt;int&gt;&gt; keyed by the same flat cell index. The
    /// dictionary was correct and compact, but it charged a full hash lookup -- two dependent,
    /// effectively random reads into a six-figure-entry table -- for every query, including the
    /// overwhelming majority that find nothing. MapWindow issues one per visible tile per frame,
    /// measured at ~9ms of a 1000ms/sec budget and the largest single item left in its draw path
    /// after the other passes were cached; MovementSystem, StatusEffectAuraSystem,
    /// ActionEffectResolver and TestCombatBehaviorSystem all query it on their own hot paths too.
    ///
    /// A flat array turns the empty-cell case -- the common one -- into a single indexed read,
    /// with no hashing and no probe sequence; only cells that actually hold someone pay the second
    /// hop into _occupantLists. Measured in the running game against a shadow Dictionary kept
    /// populated in lockstep, over scattered whole-map lookups with ~114,000 cells occupied:
    /// 63-74ns per dictionary lookup against 29-32ns per array lookup, roughly 2.1x.
    ///
    /// That full factor only shows up for the scattered queries -- MovementSystem's collision
    /// checks, StatusEffectAuraSystem's radius scans, ActionEffectResolver's target walks.
    /// MapWindow gains less (~3ms/sec of its own draw): its viewport is a small enough slice of
    /// the map to stay largely cache-resident either way, which is also why an A/B of column- vs
    /// row-major traversal found no difference between the two orders.
    ///
    /// The cost is Size.Volume ints -- 12MB at FloorBuilder's 1000x1000x3 TestMapSize, matching
    /// _blockingEntityIds beside it. Deliberately a primitive array rather than the more obvious
    /// List&lt;int&gt;?[]: an array of three million references is scanned in full by every gen2 GC,
    /// which would have handed back a chunk of what this change is buying.
    /// </remarks>
    private readonly int[] _occupantListIndexByCell;

    /// <summary>
    /// The dense side of the occupant index: one entry per currently-occupied cell, addressed by
    /// _occupantListIndexByCell. Only as large as the map is actually populated (tens of
    /// thousands of entries, not millions), so unlike the per-cell array above this one is small
    /// enough to hold references without the GC-scanning cost that implies.
    /// </summary>
    private readonly List<List<int>> _occupantLists = [];

    /// <summary>
    /// Slots in _occupantLists whose cell has emptied, ready to be claimed by the next cell that
    /// gains an occupant.
    /// </summary>
    /// <remarks>
    /// Recycling matters here for the same reason it did under the dictionary: a wandering
    /// population (e.g. Ghosts, genuinely exempt from collision) moves every cell it visits
    /// through exactly this empty-then-repopulate cycle, so without recycling nearly every move
    /// would abandon one List (plus its backing array) and allocate a fresh one -- avoidable
    /// garbage at this population and move rate. Recycling the slot INDEX rather than the List
    /// object is strictly better than the old Stack&lt;List&lt;int&gt;&gt; did: the freed slot keeps its
    /// own already-emptied list, backing array and Capacity intact, so claiming it back costs
    /// nothing at all and the first re-add doesn't reallocate.
    /// </remarks>
    private readonly Stack<int> _freeOccupantListIndices = new();

    private static readonly List<int> EmptyEntityIds = [];

    /// <summary>Maximum map depth GetOccupiedLayerMask's own byte-per-column encoding can represent -- one bit per MapLayer. MapLayer has three values today (see FloorBuilder's own 1000x1000x3 TestMapSize), so this is deliberately generous headroom rather than a live constraint; the constructor enforces it so a genuinely deeper map fails loudly here instead of silently losing its upper layers' badges.</summary>
    private const int MaximumMaskableLayers = 8;

    /// <summary>
    /// One bit per MapLayer per (x, y) column: set when that exact (x, y, layer) cell currently
    /// holds at least one occupant. A pure derived index over the occupant store, maintained by
    /// Add/RemoveOccupantEntityId alongside it so the two can't drift.
    /// </summary>
    /// <remarks>
    /// Exists for MapWindow.DrawLayerBadges, which asks "is any layer above/below this one
    /// occupied?" for every visible tile, every frame. Answering that through
    /// GetOccupantEntityIdsAt meant Size.Z - 1 separate occupant lookups per tile (at the time,
    /// into a hundred-thousand-entry Dictionary), and at this game's real layer density it found nothing
    /// the overwhelming majority of the time -- measured at 17.4ms of a 1000ms/sec budget, the
    /// single largest item in MapWindow's entire draw path and roughly a fifth of it. One array
    /// read plus two bit tests replaces the whole scan.
    ///
    /// Sized X * Y (one byte per column, not per cell), so it costs a megabyte at TestMapSize --
    /// an order of magnitude less than the Blocking fast-path index it sits beside, and read with
    /// far better locality than the dictionary it replaces since a viewport row walks contiguous
    /// bytes.
    /// </remarks>
    private readonly byte[] _occupiedLayerMaskByColumn;

    public Map(Vector3Int size)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size.Z, MaximumMaskableLayers, nameof(size));

        Size = size;

        _blockingEntityIds = new int[size.Volume];
        Array.Fill(_blockingEntityIds, -1);

        _terrainEntityIds = new int[size.X * size.Y * TerrainLayerCount];
        Array.Fill(_terrainEntityIds, -1);

        _occupantListIndexByCell = new int[size.Volume];
        Array.Fill(_occupantListIndexByCell, NoOccupantList);

        _occupiedLayerMaskByColumn = new byte[size.X * size.Y];
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
    /// thousands of short-lived allocations a frame, against the heap this game's ~2.6M-entity
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
    /// The off-map check is deliberate rather than incidental. The Dictionary this replaced
    /// answered an off-map position with a plain miss, and callers rely on that tolerance
    /// (ActionEffectResolver and TestCombatBehaviorSystem both walk resolved target tiles without
    /// re-checking bounds first) -- a bare array index would throw instead. One unsigned compare
    /// catches both negatives and past-the-end. It does NOT catch a coordinate individually out of
    /// range that still flattens into some other valid cell (x: -1, y: 5); FlatIndex has always
    /// aliased that way and the dictionary keyed on the same value, so that behaviour is unchanged
    /// rather than newly wrong.
    /// </remarks>
    private bool TryGetOccupantList(Vector3Int position, out List<int> entityIds)
    {
        var cellIndex = position.FlatIndex(Size);
        if ((uint)cellIndex >= (uint)_occupantListIndexByCell.Length)
        {
            entityIds = EmptyEntityIds;
            return false;
        }

        var listIndex = _occupantListIndexByCell[cellIndex];
        if (listIndex == NoOccupantList)
        {
            entityIds = EmptyEntityIds;
            return false;
        }

        entityIds = _occupantLists[listIndex];
        return true;
    }

    /// <summary>Adds an entity to the occupant index at the specified position.</summary>
    /// <param name="position">The position to add the entity to.</param>
    /// <param name="entityId">The ID of the entity to add.</param>
    public void AddOccupantEntityId(Vector3Int position, int entityId)
    {
        var cellIndex = position.FlatIndex(Size);
        var listIndex = _occupantListIndexByCell[cellIndex];

        if (listIndex == NoOccupantList)
        {
            if (_freeOccupantListIndices.Count > 0)
            {
                listIndex = _freeOccupantListIndices.Pop();
            }
            else
            {
                listIndex = _occupantLists.Count;
                _occupantLists.Add([]);
            }

            _occupantListIndexByCell[cellIndex] = listIndex;
        }

        _occupantLists[listIndex].Add(entityId);
        _occupiedLayerMaskByColumn[ColumnIndex(position)] |= LayerBit(position.Z);
    }

    /// <summary>Removes an entity from the occupant index at the specified position.</summary>
    /// <remarks>No-ops if entityId isn't actually recorded at position -- mirrors ClearIfOccupiedBy's own tolerance.</remarks>
    /// <param name="position">The position from which to remove the entity.</param>
    /// <param name="entityId">The ID of the entity to remove.</param>
    public void RemoveOccupantEntityId(Vector3Int position, int entityId)
    {
        var cellIndex = position.FlatIndex(Size);
        var listIndex = _occupantListIndexByCell[cellIndex];
        if (listIndex == NoOccupantList)
        {
            return;
        }

        var entityIds = _occupantLists[listIndex];
        entityIds.Remove(entityId);
        if (entityIds.Count == 0)
        {
            // The list stays attached to its slot rather than being handed back separately -- it is
            // already empty, and keeping it means the next cell to claim this slot inherits its
            // backing array and Capacity for free. See _freeOccupantListIndices.
            _occupantListIndexByCell[cellIndex] = NoOccupantList;
            _freeOccupantListIndices.Push(listIndex);

            // Cleared only when the cell empties completely -- the bit means "this cell has at
            // least one occupant," so it must survive a removal that leaves others behind.
            _occupiedLayerMaskByColumn[ColumnIndex(position)] &= (byte)~LayerBit(position.Z);
        }
    }

    /// <summary>
    /// A bitmask of which MapLayers currently hold at least one occupant at (x, y) -- bit N is
    /// layer N. The O(1) answer to "is any other layer of this column occupied," without a
    /// per-layer occupant lookup; see _occupiedLayerMaskByColumn for why this exists.
    /// </summary>
    /// <param name="x">The x-coordinate of the column.</param>
    /// <param name="y">The y-coordinate of the column.</param>
    public byte GetOccupiedLayerMask(int x, int y) => _occupiedLayerMaskByColumn[x + y * Size.X];

    /// <summary>Index into the per-column (X * Y) mask array -- deliberately not FlatIndex, which is per-cell (X * Y * Z) and includes the Z term this array has collapsed into its bits.</summary>
    private int ColumnIndex(Vector3Int position) => position.X + position.Y * Size.X;

    private static byte LayerBit(int layer) => (byte)(1 << layer);

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