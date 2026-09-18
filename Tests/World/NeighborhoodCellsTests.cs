using Engine.Math;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class NeighborhoodCellsTests
{
    [TestMethod]
    public void SameOffsetInDifferentNeighborhoods_AreDistinctCells()
    {
        var cells = new NeighborhoodCells<int>();

        cells.Set(new Vector3Int(5, 5, 0), 1);
        cells.Set(new Vector3Int(1029, 5, 0), 2);
        cells.Set(new Vector3Int(5 - 1024, 5, 0), 3);
        cells.Set(new Vector3Int(5, 5, 1), 4);

        Assert.AreEqual(1, cells.GetValueOrDefault(new Vector3Int(5, 5, 0)));
        Assert.AreEqual(2, cells.GetValueOrDefault(new Vector3Int(1029, 5, 0)));
        Assert.AreEqual(3, cells.GetValueOrDefault(new Vector3Int(-1019, 5, 0)));
        Assert.AreEqual(4, cells.GetValueOrDefault(new Vector3Int(5, 5, 1)));
        Assert.AreEqual(4, cells.Count);
    }

    [TestMethod]
    public void Set_ExistingCell_ReplacesWithoutCountingTwice()
    {
        var cells = new NeighborhoodCells<int>();

        cells.Set(new Vector3Int(1023, 1023, 2), 1);
        cells.Set(new Vector3Int(1023, 1023, 2), 7);

        Assert.AreEqual(7, cells.GetValueOrDefault(new Vector3Int(1023, 1023, 2)));
        Assert.AreEqual(1, cells.Count);
    }

    [TestMethod]
    public void Remove_DropsOnlyThatCell()
    {
        var cells = new NeighborhoodCells<int>();
        cells.Set(new Vector3Int(1, 1, 0), 1);
        cells.Set(new Vector3Int(2, 1, 0), 2);

        cells.Remove(new Vector3Int(1, 1, 0));
        cells.Remove(new Vector3Int(3000, 3000, 0));

        Assert.IsFalse(cells.TryGetValue(new Vector3Int(1, 1, 0), out _));
        Assert.AreEqual(2, cells.GetValueOrDefault(new Vector3Int(2, 1, 0)));
        Assert.AreEqual(1, cells.Count);
    }

    /// <summary>The neighborhood just read is cached; dropping it must not leave that cache answering for it.</summary>
    [TestMethod]
    public void RemoveNeighborhood_DropsItsCellsOnly_EvenRightAfterReadingIt()
    {
        var cells = new NeighborhoodCells<int>();
        cells.Set(new Vector3Int(10, 10, 0), 1);
        cells.Set(new Vector3Int(20, 20, 1), 2);
        cells.Set(new Vector3Int(1034, 10, 0), 3);
        Assert.AreEqual(1, cells.GetValueOrDefault(new Vector3Int(10, 10, 0)));

        cells.RemoveNeighborhood(0, 0);

        Assert.IsFalse(cells.TryGetValue(new Vector3Int(10, 10, 0), out _));
        Assert.IsFalse(cells.TryGetValue(new Vector3Int(20, 20, 1), out _));
        Assert.AreEqual(3, cells.GetValueOrDefault(new Vector3Int(1034, 10, 0)));
        Assert.AreEqual(1, cells.Count);

        cells.Set(new Vector3Int(10, 10, 0), 5);
        Assert.AreEqual(5, cells.GetValueOrDefault(new Vector3Int(10, 10, 0)));
        Assert.AreEqual(2, cells.Count);
    }
}
