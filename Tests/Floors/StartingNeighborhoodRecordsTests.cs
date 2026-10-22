using Engine.Math;
using Game;
using Game.Bootstrap;
using Game.Floors;

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

    /// <summary>A session draws its neighborhood records from the sequence it builds its modules with, after the build: the build itself must draw nothing from it, or the records would depend on the modules.</summary>
    [TestMethod]
    public void BuildingTheGame_DrawsNothingFromTheSessionsSequence()
    {
        const int seed = 42;
        var sessionSequence = new MathUtility(new SeededRandom((ulong)(uint)seed));

        GameBootstrapper.Build(ValidatedMods.None, GameBuildPass.CreatePlaceholderMap(), sessionSequence, initialEntityCapacity: 100, initialComponentCapacity: 50, buildsStagingWorldOnFirstRebuild: true);

        Assert.AreEqual(new MathUtility(new SeededRandom((ulong)(uint)seed)).Next(int.MinValue, int.MaxValue), sessionSequence.Next(int.MinValue, int.MaxValue));
    }

    [TestMethod]
    public void EachSeed_GivesItsOwnRecords_AndTheSameSeedTheSameOnes()
    {
        var first = FloorBuilder.StartingNeighborhoodRecords(7).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();
        var again = FloorBuilder.StartingNeighborhoodRecords(7).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();
        var other = FloorBuilder.StartingNeighborhoodRecords(8).Select(entry => (entry.Record.CellX, entry.Record.CellY, entry.Record.Seed, entry.PopulationSeed)).ToList();

        CollectionAssert.AreEqual(first, again);
        CollectionAssert.AreNotEqual(first, other);
        Assert.HasCount(9, first);
    }
}
