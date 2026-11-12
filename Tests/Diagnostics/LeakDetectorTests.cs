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
        var detector = new LeakDetector(entityManager, componentManager, populations: null);

        detector.Tick();

        Assert.HasCount(1, detector.History);
    }

    [TestMethod]
    public void Tick_FewerThanMinimumSamples_ReportsNoFindings()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        var entityManager = new EntityManager(componentManager, initialCapacity: 10);
        var detector = new LeakDetector(entityManager, componentManager, populations: null);

        detector.Tick();

        Assert.IsEmpty(detector.Findings);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Tick_SamplesTheHeapAsTheLastCollectionLeftIt_NotWhatHasBeenAllocatedSince()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        var entityManager = new EntityManager(componentManager, initialCapacity: 10);
        var detector = new LeakDetector(entityManager, componentManager, populations: null);
        GC.Collect();
        var lastCollection = GC.GetGCMemoryInfo(GCKind.Any);
        var garbageSinceTheCollection = new byte[64 * 1024];

        detector.Tick();

        if (GC.GetGCMemoryInfo(GCKind.Any).Index != lastCollection.Index)
        {
            Assert.Inconclusive("Another collection ran while sampling.");
        }

        Assert.AreEqual(lastCollection.HeapSizeBytes - lastCollection.FragmentedBytes, detector.History.Single().HeapBytesAfterLastCollection);
        GC.KeepAlive(garbageSinceTheCollection);
    }
}
