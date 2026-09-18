using Engine.Math;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class MapTests
{
    [TestMethod]
    public void GetEntityId_NewMap_EveryCellStartsEmpty()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        Assert.AreEqual(-1, map.GetBlockingEntityId(new Vector3Int(1, 1, 1)));
    }

    [TestMethod]
    public void SetEntityId_ThenGetEntityId_RoundTrips()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetBlockingEntityId(new Vector3Int(1, 2, 0), 7);

        Assert.AreEqual(7, map.GetBlockingEntityId(new Vector3Int(1, 2, 0)));
    }

    [TestMethod]
    public void ClearIfOccupiedBy_MatchingEntityId_ClearsAndReturnsTrue()
    {
        var map = new Map(new Vector3Int(3, 3, 3));
        map.SetBlockingEntityId(new Vector3Int(1, 1, 1), 5);

        var cleared = map.ClearBlockingIfOccupiedBy(new Vector3Int(1, 1, 1), 5);

        Assert.IsTrue(cleared);
        Assert.AreEqual(-1, map.GetBlockingEntityId(new Vector3Int(1, 1, 1)));
    }

    /// <summary>
    /// The compare-and-clear guard MoveEntity relies on to avoid corrupting a different
    /// entity's occupancy when a caller passes a stale old position (see WorldTests).
    /// </summary>
    [TestMethod]
    public void ClearIfOccupiedBy_DifferentEntityId_DoesNotClearAndReturnsFalse()
    {
        var map = new Map(new Vector3Int(3, 3, 3));
        map.SetBlockingEntityId(new Vector3Int(1, 1, 1), 5);

        var cleared = map.ClearBlockingIfOccupiedBy(new Vector3Int(1, 1, 1), 6);

        Assert.IsFalse(cleared);
        Assert.AreEqual(5, map.GetBlockingEntityId(new Vector3Int(1, 1, 1)));
    }

    [TestMethod]
    public void GetTerrain_NewMap_EveryCellStartsEmpty()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        Assert.IsTrue(map.GetTerrain(1, 1, TerrainLayer.Ground).IsEmpty);
    }

    [TestMethod]
    public void SetTerrain_ThenGetTerrain_RoundTripsTypeAndVariant()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrain(1, 1, TerrainLayer.Ground, new TerrainCell(4, 2));

        Assert.AreEqual(new TerrainCell(4, 2), map.GetTerrain(1, 1, TerrainLayer.Ground));
    }

    /// <summary>Terrain and creature occupancy are independent stores, so a wall standing on stone floor at the same (x,y) disturbs neither.</summary>
    [TestMethod]
    public void SetBlockingEntityId_AndSetTerrain_SameCell_DoNotClobberEachOther()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrain(1, 1, TerrainLayer.Ground, new TerrainCell(4, 0)); // e.g. StoneFloor.
        map.SetBlockingEntityId(new Vector3Int(1, 1, (int)MapLayer.Ground), 9); // e.g. Wall.

        Assert.AreEqual(new TerrainCell(4, 0), map.GetTerrain(1, 1, TerrainLayer.Ground));
        Assert.AreEqual(9, map.GetBlockingEntityId(new Vector3Int(1, 1, (int)MapLayer.Ground)));
    }

    [TestMethod]
    public void SetStructure_IsIndependentOfTerrainOccupancyAndOtherLayers()
    {
        var map = new Map(new Vector3Int(3, 3, 3));
        var groundCell = new Vector3Int(1, 1, (int)MapLayer.Ground);

        map.SetTerrain(1, 1, TerrainLayer.Ground, new TerrainCell(4, 0));
        map.SetBlockingEntityId(groundCell, 9);
        map.SetStructure(groundCell, new TerrainCell(5, 2));

        Assert.AreEqual(new TerrainCell(5, 2), map.GetStructure(groundCell));
        Assert.AreEqual((ushort)5, map.GetStructureTypeId(groundCell));
        Assert.AreEqual(new TerrainCell(4, 0), map.GetTerrain(1, 1, TerrainLayer.Ground));
        Assert.AreEqual(9, map.GetBlockingEntityId(groundCell));
        Assert.IsTrue(map.GetStructure(new Vector3Int(1, 1, (int)MapLayer.Flying)).IsEmpty);
    }

    /// <summary>A 1030x1030 map covers four neighborhoods, three of them cut off by the map's edge -- cells either side of every neighborhood edge, and in the partial neighborhoods' last cells, are all distinct.</summary>
    [TestMethod]
    public void CellsAcrossNeighborhoodEdges_AndInPartialNeighborhoods_AreIndependent()
    {
        var map = new Map(new Vector3Int(1030, 1030, 2));
        Vector3Int[] cells = [new(1023, 1023, 1), new(1024, 1023, 1), new(1023, 1024, 1), new(1024, 1024, 1), new(1029, 1029, 1), new(1029, 0, 0), new(0, 1029, 0)];

        for (var i = 0; i < cells.Length; i++)
        {
            map.SetBlockingEntityId(cells[i], 100 + i);
            map.AddOccupantEntityId(cells[i], 200 + i);
            map.SetStructure(cells[i], new TerrainCell((ushort)(300 + i), (byte)i));
            map.SetTerrain(cells[i].X, cells[i].Y, TerrainLayer.Ground, new TerrainCell((ushort)(400 + i), 0));
        }

        for (var i = 0; i < cells.Length; i++)
        {
            Assert.AreEqual(100 + i, map.GetBlockingEntityId(cells[i]));
            CollectionAssert.AreEqual(new[] { 200 + i }, map.GetOccupantEntityIdsAt(cells[i]).ToArray());
            Assert.AreEqual(new TerrainCell((ushort)(300 + i), (byte)i), map.GetStructure(cells[i]));
            Assert.AreEqual((ushort)(400 + i), map.GetTerrain(cells[i].X, cells[i].Y, TerrainLayer.Ground).TypeId);
        }

        Assert.AreEqual(0b10, map.GetOccupiedLayerMask(1024, 1024));
        Assert.AreEqual(0b01, map.GetOccupiedLayerMask(1029, 0));
        Assert.AreEqual(0, map.GetOccupiedLayerMask(1025, 1024));
    }

    /// <summary>Each coordinate is bounds-checked on its own, so an off-map X doesn't alias into a cell on the row above.</summary>
    [TestMethod]
    public void GetOccupantEntityIdsAt_AnyCoordinateOffTheMap_IsEmpty()
    {
        var map = new Map(new Vector3Int(3, 3, 2));
        map.AddOccupantEntityId(new Vector3Int(2, 0, 0), 5);

        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(-1, 1, 0)));
        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(3, 0, 0)));
        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(2, 0, 2)));
        Assert.IsFalse(map.HasOccupantAt(new Vector3Int(2, -1, 0)));
    }

    [TestMethod]
    public void GetTerrain_UnderGroundAndGroundLayers_AreIndependent()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrain(1, 1, TerrainLayer.UnderGround, new TerrainCell(1, 0));
        map.SetTerrain(1, 1, TerrainLayer.Ground, new TerrainCell(2, 0));

        Assert.AreEqual((ushort)1, map.GetTerrain(1, 1, TerrainLayer.UnderGround).TypeId);
        Assert.AreEqual((ushort)2, map.GetTerrain(1, 1, TerrainLayer.Ground).TypeId);
    }

    /// <summary>A map centred on neighborhood (0, 0): cells either side of zero, and at both corners, are distinct -- tile -1 is the last cell of neighborhood -1, not an alias of cell 1023 in neighborhood 0.</summary>
    [TestMethod]
    public void NegativeBounds_CellsEitherSideOfZero_AreIndependent()
    {
        var map = new Map(new MapBounds(-1024, -1024, 2048, 2048, 2));
        Vector3Int[] cells = [new(-1, -1, 1), new(0, 0, 1), new(1023, -1, 1), new(-1, 1023, 1), new(-1024, -1024, 0), new(2047, 2047, 0), new(-1, 0, 0), new(1023, 0, 0)];

        for (var i = 0; i < cells.Length; i++)
        {
            map.SetBlockingEntityId(cells[i], 100 + i);
            map.AddOccupantEntityId(cells[i], 200 + i);
            map.SetStructure(cells[i], new TerrainCell((ushort)(300 + i), (byte)i));
            map.SetTerrain(cells[i].X, cells[i].Y, TerrainLayer.Ground, new TerrainCell((ushort)(400 + i), 0));
        }

        for (var i = 0; i < cells.Length; i++)
        {
            Assert.AreEqual(100 + i, map.GetBlockingEntityId(cells[i]));
            CollectionAssert.AreEqual(new[] { 200 + i }, map.GetOccupantEntityIdsAt(cells[i]).ToArray());
            Assert.AreEqual(new TerrainCell((ushort)(300 + i), (byte)i), map.GetStructure(cells[i]));
            Assert.AreEqual((ushort)(400 + i), map.GetTerrain(cells[i].X, cells[i].Y, TerrainLayer.Ground).TypeId);
        }

        Assert.AreEqual(0b10, map.GetOccupiedLayerMask(-1, -1));
        Assert.AreEqual(0, map.GetOccupiedLayerMask(1023, 1023));
    }

    [TestMethod]
    public void GetOccupantEntityIdsAt_JustOutsideNegativeBounds_IsEmpty()
    {
        var map = new Map(new MapBounds(-1024, -1024, 0, 0, 1));
        map.AddOccupantEntityId(new Vector3Int(-1024, -1, 0), 5);

        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(-1025, -1, 0)));
        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(0, -1, 0)));
        Assert.IsEmpty(map.GetOccupantEntityIdsAt(new Vector3Int(-1024, 0, 0)));
        Assert.IsFalse(map.HasOccupantAt(new Vector3Int(TransformComponent.UnplacedCoordinate, TransformComponent.UnplacedCoordinate, 0)));
    }

    [TestMethod]
    [DataRow(-1000, -1024)]
    [DataRow(-1024, 5)]
    public void Bounds_NotStartingOnANeighborhoodBoundary_Throws(int minX, int minY) =>
        Assert.ThrowsExactly<ArgumentException>(() => new Map(new MapBounds(minX, minY, 1024, 1024, 1)));

    [TestMethod]
    public void UnloadNeighborhood_ItsCellsReadAsEmptyAndOffTheMap_AndRemovalsThereAreNoOps()
    {
        var map = new Map(new MapBounds(-1024, 0, 1024, 10, 2));
        var cell = new Vector3Int(-5, 5, 1);
        map.SetBlockingEntityId(cell, 7);
        map.AddOccupantEntityId(cell, 7);
        map.SetTerrain(cell.X, cell.Y, TerrainLayer.Ground, new TerrainCell(3, 0));

        map.UnloadNeighborhood(-1, 0);

        Assert.IsFalse(map.IsNeighborhoodLoaded(-1, 0));
        Assert.IsTrue(map.IsNeighborhoodLoaded(0, 0));
        Assert.IsFalse(map.Contains(cell));
        Assert.IsTrue(map.Contains(new Vector3Int(5, 5, 1)));
        Assert.AreEqual(-1, map.GetBlockingEntityId(cell));
        Assert.IsEmpty(map.GetOccupantEntityIdsAt(cell));
        Assert.IsTrue(map.GetTerrain(cell.X, cell.Y, TerrainLayer.Ground).IsEmpty);
        Assert.AreEqual(0, map.GetOccupiedLayerMask(cell.X, cell.Y));
        Assert.IsFalse(map.ClearBlockingIfOccupiedBy(cell, 7));
        map.RemoveOccupantEntityId(cell, 7);
    }

    [TestMethod]
    public void LoadNeighborhood_AfterUnloading_IsEmpty_AndLoadingTwiceThrows()
    {
        var map = new Map(new MapBounds(0, 0, 1024, 10, 2));
        map.SetBlockingEntityId(new Vector3Int(3, 3, 0), 9);
        map.UnloadNeighborhood(0, 0);

        map.LoadNeighborhood(0, 0);

        Assert.AreEqual(-1, map.GetBlockingEntityId(new Vector3Int(3, 3, 0)));
        Assert.ThrowsExactly<InvalidOperationException>(() => map.LoadNeighborhood(0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => map.LoadNeighborhood(4, 0));
    }

    [TestMethod]
    public void Unbounded_LoadsWholeNeighborhoodsAnywhere_AndBoundsFollowWhatIsLoaded()
    {
        var map = Map.Unbounded(depth: 2);
        Assert.IsFalse(map.Contains(new Vector3Int(5, 5, 0)), "Nothing is loaded yet.");

        map.LoadNeighborhood(-3, 7);
        map.LoadNeighborhood(2, 7);

        Assert.AreEqual(new MapBounds(-3 * 1024, 7 * 1024, 3 * 1024, 8 * 1024, 2), map.Bounds);
        Assert.IsTrue(map.Contains(new Vector3Int(-3 * 1024 + 1023, 7 * 1024 + 1023, 1)));
        Assert.IsFalse(map.Contains(new Vector3Int(0, 7 * 1024, 0)), "Inside the rectangle, but not loaded.");
        Assert.IsTrue(map.CanHold(1_000, -1_000));

        map.UnloadNeighborhood(-3, 7);

        Assert.AreEqual(new MapBounds(2 * 1024, 7 * 1024, 3 * 1024, 8 * 1024, 2), map.Bounds);
    }

    /// <summary>The fast lookup covers only the square around its centre; a neighborhood outside it still reads and writes the same, through the dictionary, and moving the lookup changes nothing stored.</summary>
    [TestMethod]
    public void Unbounded_NeighborhoodOutsideTheLookupSquare_StillReadsAndWrites_AndRecentringKeepsEverything()
    {
        var map = Map.Unbounded(depth: 1, lookupGridSide: 3);
        map.LoadNeighborhood(0, 0);
        map.LoadNeighborhood(9, 0);
        var near = new Vector3Int(5, 5, 0);
        var far = new Vector3Int(9 * 1024 + 5, 5, 0);
        map.SetBlockingEntityId(near, 1);
        map.SetBlockingEntityId(far, 2);

        Assert.AreEqual(2, map.GetBlockingEntityId(far));

        map.CenterLookupOn(9, 0);

        Assert.AreEqual(1, map.GetBlockingEntityId(near));
        Assert.AreEqual(2, map.GetBlockingEntityId(far));
    }

    [TestMethod]
    public void Bounded_CanHoldOnlyItsDeclaredRectangle()
    {
        var map = new Map(new MapBounds(-1024, 0, 1024, 10, 1));

        Assert.IsTrue(map.CanHold(-1, 0));
        Assert.IsFalse(map.CanHold(1, 0));
        Assert.IsFalse(map.CanHold(0, -1));
        Assert.IsTrue(map.IsBounded);
    }
}
