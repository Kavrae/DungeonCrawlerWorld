using Engine.Diagnostics;
using Engine.ECS.Components;

namespace Tests.Diagnostics;

[TestClass]
public sealed class ComponentMemoryTrackerTests
{
    private struct SmallComponent
    {
        public int Value;
    }

    private struct LargeComponent
    {
        public long Value;
    }

    [TestMethod]
    public void Tick_FirstCall_SamplesImmediatelyAndSortsDescendingByBytes()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        componentManager.RegisterDirectPool<SmallComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<LargeComponent>(static (ref existing, incoming) => existing = incoming);

        var tracker = new ComponentMemoryTracker(componentManager);
        tracker.Tick();

        Assert.AreEqual(2, tracker.Snapshot.Count);
        Assert.AreEqual(nameof(LargeComponent), tracker.Snapshot[0].ComponentTypeName);
        Assert.AreEqual(nameof(SmallComponent), tracker.Snapshot[1].ComponentTypeName);
        Assert.IsTrue(tracker.Snapshot[0].EstimatedBytes > tracker.Snapshot[1].EstimatedBytes);
    }

    [TestMethod]
    public void Tick_ReflectsCurrentCount()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        componentManager.RegisterDirectPool<SmallComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.Merge(entityId: 0, new SmallComponent { Value = 1 });
        componentManager.Merge(entityId: 1, new SmallComponent { Value = 2 });

        var tracker = new ComponentMemoryTracker(componentManager);
        tracker.Tick();

        Assert.AreEqual(2, tracker.Snapshot[0].Count);
    }
}
