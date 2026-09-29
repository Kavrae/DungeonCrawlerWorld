using Engine.Diagnostics;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Bootstrap;

[TestClass]
public sealed class GameBuildPassDiagnosticsTests
{
    private static bool HasGauge(GaugeRegistry gauges, string groupName, string gaugeName) =>
        gauges.Gauges.Any(gauge => gauge.GroupName == groupName && gauge.GaugeName == gaugeName);

    [TestMethod]
    public void Run_GivesDiagnosticsTheBuiltPopulationAndTheGameGauges()
    {
        var ecsContext = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3))).EcsContext;

        var populations = ecsContext.EntityPopulations!;
        Assert.AreEqual("Built", populations.PartialPopulationName);
        Assert.IsTrue(populations.IsHeldByEveryEntity(typeof(SpawnRecordComponent)));
        Assert.IsFalse(populations.IsHeldByEveryEntity(typeof(SimpleHealthComponent)));
        Assert.IsTrue(HasGauge(ecsContext.Gauges, "Entities", "Built"));
        Assert.IsTrue(HasGauge(ecsContext.Gauges, "Creatures", "Skeletons"));
        Assert.IsTrue(HasGauge(ecsContext.Gauges, "ProcessingTier", "Local"));
        Assert.IsTrue(HasGauge(ecsContext.Gauges, "ProcessingTier", "PendingTransitionNeighborhoods"));
    }

    [TestMethod]
    public void BuildModules_GivesDiagnosticsNoGamePopulationOrGauges()
    {
        var ecsContext = BuiltInTestModules.BuildModules([]).EcsContext;

        Assert.IsNull(ecsContext.EntityPopulations);
        Assert.IsFalse(HasGauge(ecsContext.Gauges, "Entities", "Built"));
        Assert.IsFalse(ecsContext.Gauges.Gauges.Any(gauge => gauge.GroupName is "Creatures" or "ProcessingTier"));
    }

    [TestMethod]
    [DoNotParallelize]
    public void EntityPopulations_SetAfterTheSessionBegan_Throws()
    {
        using var ecsContext = BuiltInTestModules.BuildModules([]).EcsContext;
        ecsContext.BeginSession();

        Assert.ThrowsExactly<InvalidOperationException>(() => ecsContext.EntityPopulations = null);
    }
}
