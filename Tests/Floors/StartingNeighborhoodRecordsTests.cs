using Game;
using Game.Floors;
using Game.World;

namespace Tests.Floors;

/// <summary>FloorBuilder.StartingNeighborhoodRecords gives each starting-window neighborhood the record and population seed a session seeded the same way gives it -- what a tool's preview of a seeded world rests on.</summary>
[TestClass]
public sealed class StartingNeighborhoodRecordsTests
{
    [TestMethod]
    public void TheStartingWindow_IsTheDefaultMapsLoadedNeighborhoods_InPopulatesOrder()
    {
        var map = FloorBuilder.CreateMap(floorNumber: 0);

        CollectionAssert.AreEqual(TestMapBuilder.LoadedNeighborhoods(map).ToList(), FloorBuilder.StartingWindowNeighborhoods().ToList());
    }

    [TestMethod]
    public void EachSeed_GivesItsOwnRecords_AndTheSameSeedTheSameOnes()
    {
        var first = FloorBuilder.StartingNeighborhoodRecords(7, FloorBuilder.FirstFloorNumber).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();
        var again = FloorBuilder.StartingNeighborhoodRecords(7, FloorBuilder.FirstFloorNumber).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();
        var other = FloorBuilder.StartingNeighborhoodRecords(8, FloorBuilder.FirstFloorNumber).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();

        CollectionAssert.AreEqual(first, again);
        CollectionAssert.AreNotEqual(first, other);
        Assert.HasCount(9, first);
    }

    [TestMethod]
    public void EachRecord_HasItsDerivedLayoutSeed_AndItsFirstPopulationSeed()
    {
        foreach (var (record, populationSeed) in FloorBuilder.StartingNeighborhoodRecords(7, FloorBuilder.FirstFloorNumber))
        {
            Assert.AreEqual(NeighborhoodSeeds.ForLayout(7, FloorBuilder.FirstFloorNumber, record.CellX, record.CellY), record.Seed);
            Assert.AreEqual(NeighborhoodSeeds.ForPopulation(record.Seed, 1), populationSeed);
        }
    }
}
