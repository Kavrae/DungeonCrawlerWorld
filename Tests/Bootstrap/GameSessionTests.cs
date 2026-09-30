using Engine.Math;
using Game.Bootstrap;
using Game.World;

namespace Tests.Bootstrap;

[TestClass]
public sealed class GameSessionTests
{
    private static GameSession Build() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(5, 5, 1)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);

    [TestMethod]
    public void SimulationClock_IsTheClockTheSystemManagerAdvances()
    {
        var session = Build();

        Assert.AreSame(session.EcsContext.SystemManager.Clock, session.SimulationClock);
    }

    [TestMethod]
    public void Skeletons_AreTheFactorysOwn()
    {
        var session = Build();

        Assert.AreSame(session.Internals.Factory.Skeletons, session.Internals.Skeletons);
    }

    [TestMethod]
    public void Catalogs_HoldWhatTheModulesConfigured()
    {
        var session = Build();

        Assert.IsGreaterThan(0, session.Catalogs.ItemCatalog.Count);
        Assert.IsGreaterThan(0, session.Catalogs.LootboxCatalog.Count);
    }
}
