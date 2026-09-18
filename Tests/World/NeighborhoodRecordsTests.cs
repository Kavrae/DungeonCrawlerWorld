using Engine.Math;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class NeighborhoodRecordsTests
{
    [TestMethod]
    public void GetOrCreate_SameCoordinate_ReturnsTheSameRecord()
    {
        var records = new NeighborhoodRecords(new MathUtility(new Random(1)));

        var first = records.GetOrCreate(-1, 2);

        Assert.AreSame(first, records.GetOrCreate(-1, 2));
        Assert.AreEqual(1, records.Count);
    }

    [TestMethod]
    public void GetOrCreate_SameSessionSeedAndOrder_AssignsTheSameSeeds()
    {
        var a = new NeighborhoodRecords(new MathUtility(new Random(5)));
        var b = new NeighborhoodRecords(new MathUtility(new Random(5)));

        Assert.AreEqual(a.GetOrCreate(0, 0).Seed, b.GetOrCreate(0, 0).Seed);
        Assert.AreEqual(a.GetOrCreate(1, 0).Seed, b.GetOrCreate(1, 0).Seed);
        Assert.AreNotEqual(a.GetOrCreate(0, 0).Seed, a.GetOrCreate(1, 0).Seed);
    }

    [TestMethod]
    public void NextPopulationSeed_ChangesEveryPopulation_AndRepeatsForTheSameSeed()
    {
        var record = new NeighborhoodRecord(0, 0, seed: 42);
        var twin = new NeighborhoodRecord(3, 3, seed: 42);

        var first = record.NextPopulationSeed();
        var second = record.NextPopulationSeed();

        Assert.AreNotEqual(first, second);
        Assert.AreNotEqual(42, first);
        Assert.AreEqual(first, twin.NextPopulationSeed());
        Assert.AreEqual(2, record.PopulationCount);
    }
}
