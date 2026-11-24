using Engine.Math;
using Engine.Tiles;
using Game.World;

namespace Tests.GameTests.World;

[TestClass]
public sealed class OccupiedCellScanTests
{
    private const int Depth = 3;

    [TestMethod]
    public void Scan_UnboundedMapWithAnUnloadedNeighborhood_MatchesThePerTileLoop()
    {
        var map = Map.Unbounded(Depth);
        map.LoadNeighborhood(-1, 0);
        map.LoadNeighborhood(0, 0);
        map.LoadNeighborhood(0, -1);
        PlaceSeededOccupants(map, minX: -24, minY: -24, maxXExclusive: 24, maxYExclusive: 24, seed: 1);

        foreach (var (minX, minY, maxX, maxY) in new[]
        {
            (-24, -24, 24, 24),
            (-8, 4, 8, 12),
            (-8, -8, 8, -1),
            (4, -8, 12, 8),
            (-1, -1, 1, 1),
            (-1030, -4, -1020, 4),
            (1020, -4, 1030, 4),
            (-4, 1020, 4, 1030),
            (5, 5, 6, 6),
            (5, 5, 5, 9),
            (9, 5, 5, 9),
        })
        {
            for (var mapLayer = -1; mapLayer <= Depth; mapLayer++)
            {
                AssertScanMatchesPerTileLoop(map, minX, minY, maxX, maxY, mapLayer);
            }
        }
    }

    [TestMethod]
    public void Scan_BoundedMapCutShort_MatchesThePerTileLoop()
    {
        var map = new Map(new MapBounds(-1024, 0, 10, 20, Depth));
        PlaceSeededOccupants(map, minX: -16, minY: 0, maxXExclusive: 10, maxYExclusive: 20, seed: 2);

        foreach (var (minX, minY, maxX, maxY) in new[]
        {
            (-16, 0, 10, 20),
            (-20, -4, 16, 26),
            (6, 16, 1030, 1030),
            (10, 0, 16, 20),
            (-4, 20, 4, 26),
        })
        {
            for (var mapLayer = 0; mapLayer < Depth; mapLayer++)
            {
                AssertScanMatchesPerTileLoop(map, minX, minY, maxX, maxY, mapLayer);
            }
        }
    }

    [TestMethod]
    public void Scan_AfterAddSharedMoveAndRemove_AgreesWithTheOccupantIndex()
    {
        var map = new Map(new Vector3Int(8, 8, Depth));
        var first = new Vector3Int(2, 3, 1);
        var second = new Vector3Int(3, 3, 1);

        map.AddOccupantEntityId(first, 10);
        AssertScanMatchesPerTileLoop(map, 0, 0, 8, 8, 1);

        map.AddOccupantEntityId(first, 11);
        AssertScanMatchesPerTileLoop(map, 0, 0, 8, 8, 1);

        map.RemoveOccupantEntityId(first, 10);
        AssertScanMatchesPerTileLoop(map, 0, 0, 8, 8, 1);

        Assert.IsTrue(map.TryMoveSoleOccupant(first, second, 11, isBlocking: false));
        AssertScanMatchesPerTileLoop(map, 0, 0, 8, 8, 1);

        map.RemoveOccupantEntityId(second, 11);
        AssertScanMatchesPerTileLoop(map, 0, 0, 8, 8, 1);
        Assert.IsEmpty(ScanCells(map, 0, 0, 8, 8, 1));
    }

    [TestMethod]
    public void Scan_OccupiedRectangle_AllocatesNothing()
    {
        var map = new Map(new MapBounds(-1024, 0, 10, 20, Depth));
        PlaceSeededOccupants(map, minX: -16, minY: 0, maxXExclusive: 10, maxYExclusive: 20, seed: 3);

        for (var warmUp = 0; warmUp < 1000; warmUp++)
        {
            SumOccupants(map);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var total = SumOccupants(map);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsGreaterThan(0, total);
        Assert.AreEqual(0, allocated);
    }

    private static int SumOccupants(Map map)
    {
        var total = 0;
        foreach (var cell in map.GetOccupiedCellsIn(-16, 0, 10, 20, 1))
        {
            total += cell.Occupants.Length;
        }

        return total;
    }

    private static void PlaceSeededOccupants(Map map, int minX, int minY, int maxXExclusive, int maxYExclusive, int seed)
    {
        var random = new Random(seed);
        var nextEntityId = 1;
        for (var placement = 0; placement < 400; placement++)
        {
            var x = random.Next(minX, maxXExclusive);
            var y = random.Next(minY, maxYExclusive);
            var mapLayer = random.Next(Depth);
            var footprint = random.Next(4) == 0 ? 3 : 1;
            var entityId = nextEntityId++;
            for (var footprintY = y; footprintY < y + footprint; footprintY++)
            {
                for (var footprintX = x; footprintX < x + footprint; footprintX++)
                {
                    var tile = new Vector3Int(footprintX, footprintY, mapLayer);
                    if (map.Contains(tile))
                    {
                        map.AddOccupantEntityId(tile, entityId);
                    }
                }
            }
        }
    }

    private static void AssertScanMatchesPerTileLoop(Map map, int minX, int minY, int maxX, int maxY, int mapLayer)
    {
        var expected = new List<(Vector3Int Position, int[] Occupants)>();
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var position = new Vector3Int(x, y, mapLayer);
                var occupants = map.GetOccupantEntityIdSpanAt(position);
                if (!occupants.IsEmpty)
                {
                    expected.Add((position, occupants.ToArray()));
                }
            }
        }

        var scanned = ScanCells(map, minX, minY, maxX, maxY, mapLayer);
        var rectangle = $"({minX}, {minY})..({maxX}, {maxY}) layer {mapLayer}";
        Assert.HasCount(expected.Count, scanned, rectangle);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.AreEqual(expected[index].Position, scanned[index].Position, rectangle);
            CollectionAssert.AreEqual(expected[index].Occupants, scanned[index].Occupants, rectangle);
        }
    }

    private static List<(Vector3Int Position, int[] Occupants)> ScanCells(Map map, int minX, int minY, int maxX, int maxY, int mapLayer)
    {
        var cells = new List<(Vector3Int Position, int[] Occupants)>();
        foreach (var cell in map.GetOccupiedCellsIn(minX, minY, maxX, maxY, mapLayer))
        {
            cells.Add((cell.Position, cell.Occupants.ToArray()));
        }

        return cells;
    }
}
