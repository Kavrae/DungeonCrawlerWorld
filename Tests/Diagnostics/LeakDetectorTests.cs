using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Tests.Diagnostics;

[TestClass]
public sealed class LeakDetectorTests
{
    [TestMethod]
    public void Tick_FirstCall_SamplesImmediately()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        var entityManager = new EntityManager(componentManager, initialCapacity: 10);
        var detector = new LeakDetector(entityManager, componentManager);

        detector.Tick();

        Assert.HasCount(1, detector.History);
    }

    [TestMethod]
    public void Tick_FewerThanMinimumSamples_ReportsNoFindings()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        var entityManager = new EntityManager(componentManager, initialCapacity: 10);
        var detector = new LeakDetector(entityManager, componentManager);

        detector.Tick();

        Assert.IsEmpty(detector.Findings);
    }
}
