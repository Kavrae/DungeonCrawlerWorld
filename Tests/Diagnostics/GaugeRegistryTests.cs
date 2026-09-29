using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Tests.Diagnostics;

[TestClass]
[DoNotParallelize]
public sealed class GaugeRegistryTests
{
    private static EcsContext CreateContext()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        return new EcsContext(new EntityManager(componentManager, initialCapacity: 10), componentManager, new SystemManager(), new EventBus());
    }

    private static Gauge FindGauge(GaugeRegistry registry, string groupName, string gaugeName) =>
        registry.Gauges.Single(gauge => gauge.GroupName == groupName && gauge.GaugeName == gaugeName);

    [TestMethod]
    public void Register_ADuplicateName_ThrowsNamingIt()
    {
        var registry = new GaugeRegistry();
        registry.Register("Streamer", "LoadJobs", GaugeKind.Level, () => 0);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => registry.Register("Streamer", "LoadJobs", GaugeKind.Level, () => 0));

        Assert.Contains("Streamer/LoadJobs", exception.Message);
    }

    [TestMethod]
    public void Register_TheSameNameInAnotherGroup_Registers()
    {
        var registry = new GaugeRegistry();
        registry.Register("Streamer", "Jobs", GaugeKind.Level, () => 0);

        registry.Register("Tiers", "Jobs", GaugeKind.Level, () => 0);

        Assert.HasCount(2, registry.Gauges);
    }

    [TestMethod]
    public void Register_AfterTheSessionBegan_Throws()
    {
        using var context = CreateContext();
        context.BeginSession();

        Assert.ThrowsExactly<InvalidOperationException>(() => context.Gauges.Register("Late", "Gauge", GaugeKind.Level, () => 0));
    }

    [TestMethod]
    public void Dispose_ClearsTheGauges()
    {
        var context = CreateContext();
        context.BeginSession();

        context.Dispose();

        Assert.IsEmpty(context.Gauges.Gauges);
    }

    [TestMethod]
    public void ANewContext_RegistersTheEngineGauges()
    {
        using var context = CreateContext();
        context.EntityManager.CreateEntity();
        context.EntityManager.CreateEntity();

        Assert.AreEqual(2d, FindGauge(context.Gauges, "Entities", "Living").Read());
        Assert.AreEqual(context.EntityManager.Capacity, FindGauge(context.Gauges, "Entities", "Capacity").Read());
        Assert.AreEqual(GaugeKind.Level, FindGauge(context.Gauges, "Pools", "Components").Kind);
        Assert.AreEqual(GaugeKind.Level, FindGauge(context.Gauges, "Pools", "EstimatedBytes").Kind);
    }

    [TestMethod]
    public void PoolsComponents_SumsEveryPoolsCount()
    {
        using var context = CreateContext();
        context.ComponentManager.RegisterPackedPool<int>(static (ref existing, incoming) => existing = incoming);
        context.ComponentManager.RegisterPackedPool<long>(static (ref existing, incoming) => existing = incoming);
        var entityId = context.EntityManager.CreateEntity();
        context.ComponentManager.GetPackedPool<int>().Add(entityId, 1);
        context.ComponentManager.GetPackedPool<long>().Add(entityId, 1);

        Assert.AreEqual(2d, FindGauge(context.Gauges, "Pools", "Components").Read());
    }
}
