using Game.World;

namespace Tests.World;

[TestClass]
public sealed class NeighborhoodRecordsTests
{
    [TestMethod]
    public void GetOrCreate_SameCoordinate_ReturnsTheSameRecord()
    {
        var records = new NeighborhoodRecords(worldSeed: 1, floorNumber: 1);

        var first = records.GetOrCreate(-1, 2);

        Assert.AreSame(first, records.GetOrCreate(-1, 2));
        Assert.AreEqual(1, records.Count);
    }

    [TestMethod]
    public void GetOrCreate_GivesACoordinateTheSameSeed_WhateverOrderCoordinatesAreAskedFor()
    {
        var forwards = new NeighborhoodRecords(worldSeed: 5, floorNumber: 1);
        var backwards = new NeighborhoodRecords(worldSeed: 5, floorNumber: 1);
        (int CellX, int CellY)[] cells = [(0, 0), (1, 0), (-3, 7), (2, -2)];

        var forwardSeeds = cells.Select(cell => forwards.GetOrCreate(cell.CellX, cell.CellY).Seed).ToList();
        foreach (var (cellX, cellY) in cells.Reverse())
        {
            backwards.GetOrCreate(cellX, cellY);
        }

        CollectionAssert.AreEqual(forwardSeeds, cells.Select(cell => backwards.GetOrCreate(cell.CellX, cell.CellY).Seed).ToList());
    }

    [TestMethod]
    public void GetOrCreate_IsTheDerivedLayoutSeed()
    {
        var records = new NeighborhoodRecords(worldSeed: 5, floorNumber: 2);

        Assert.AreEqual(NeighborhoodSeeds.ForLayout(5, 2, -3, 7), records.GetOrCreate(-3, 7).Seed);
    }

    [TestMethod]
    public void LayoutSeeds_DifferBetweenCells_Floors_AndSessionSeeds()
    {
        var seed = NeighborhoodSeeds.ForLayout(5, 1, 0, 0);

        Assert.AreNotEqual(seed, NeighborhoodSeeds.ForLayout(5, 1, 1, 0));
        Assert.AreNotEqual(seed, NeighborhoodSeeds.ForLayout(5, 1, 0, 1));
        Assert.AreNotEqual(NeighborhoodSeeds.ForLayout(5, 1, 1, 0), NeighborhoodSeeds.ForLayout(5, 1, 0, 1));
        Assert.AreNotEqual(seed, NeighborhoodSeeds.ForLayout(5, 2, 0, 0));
        Assert.AreNotEqual(seed, NeighborhoodSeeds.ForLayout(6, 1, 0, 0));
    }

    [TestMethod]
    public void NextPopulationSeed_ChangesEveryPopulation_AndRepeatsForTheSameSeed()
    {
        var record = new NeighborhoodRecord(0, 0, seed: 42);
        var twin = new NeighborhoodRecord(3, 3, seed: 42);

        var pending = record.PendingPopulationSeed;
        var first = record.NextPopulationSeed();
        var second = record.NextPopulationSeed();

        Assert.AreEqual(pending, first);
        Assert.AreNotEqual(first, second);
        Assert.AreNotEqual(42, first);
        Assert.AreEqual(first, twin.NextPopulationSeed());
        Assert.AreEqual(2, record.PopulationCount);
    }
}
