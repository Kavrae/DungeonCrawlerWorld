using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Tests.ECS.Components;

/// <summary>ComponentChanged must fire on every write path -- the timer wheels' scheduling is only as complete as this list.</summary>
[TestClass]
public sealed class ComponentChangedTests
{
    private struct Value
    {
        public int Number { get; set; }
    }

    private readonly struct NumberMatcher(int number) : IComponentMatcher<Value>
    {
        public bool Matches(in Value value) => value.Number == number;
    }

    private readonly struct AddUpdate(int amount) : IComponentUpdater<Value>
    {
        public void Update(ref Value value) => value.Number += amount;
    }

    private readonly struct SetUpdate(int replacement) : IComponentUpdater<Value>
    {
        public void Update(ref Value value) => value.Number = replacement;
    }

    private static (PackedComponentPool<Value> Pool, List<(int EntityId, int DenseIndex)> Changes) Packed()
    {
        var pool = new PackedComponentPool<Value>(16, 4, static (ref existing, incoming) => existing.Number += incoming.Number, ComponentChangeObservation.Observed);
        var changes = new List<(int, int)>();
        pool.ComponentChanged += (entityId, denseIndex) => changes.Add((entityId, denseIndex));
        return (pool, changes);
    }

    private static (MultiComponentPool<Value> Pool, List<(int EntityId, int DenseIndex)> Changes) Multi()
    {
        var pool = new MultiComponentPool<Value>(16, 4, ComponentChangeObservation.Observed);
        var changes = new List<(int, int)>();
        pool.ComponentChanged += (entityId, denseIndex) => changes.Add((entityId, denseIndex));
        return (pool, changes);
    }

    [TestMethod]
    public void Packed_EveryWritePath_Notifies()
    {
        var (pool, changes) = Packed();

        pool.Add(3, new Value { Number = 1 });
        pool.Merge(3, new Value { Number = 1 });
        pool.Merge(5, new Value { Number = 1 });
        pool.TrySet(3, new Value { Number = 9 });
        pool.TryUpdate(3, new AddUpdate(1));
        var denseIndex = pool.GetDenseIndex(3);
        pool.SetByDenseIndex(denseIndex, new Value { Number = 1 });
        pool.UpdateByDenseIndex(denseIndex, new AddUpdate(1));
        using (var write = pool.Write(3))
        {
            write.Value.Number = 4;
        }

        using (var write = pool.TryWrite(3))
        {
            write.Value.Number = 5;
        }

        using (var write = pool.WriteByDenseIndex(denseIndex))
        {
            write.Value.Number = 6;
        }

        CollectionAssert.AreEqual(
            new[] { (3, 0), (3, 0), (5, 1), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0) },
            changes,
            "Add, Merge, Merge-as-Add, TrySet, TryUpdate, SetByDenseIndex, UpdateByDenseIndex, Write, TryWrite, WriteByDenseIndex.");
    }

    [TestMethod]
    public void Packed_RemoveAndFailedWrites_DoNotNotify()
    {
        var (pool, changes) = Packed();
        pool.Add(3, new Value());
        changes.Clear();

        pool.TrySet(4, new Value());
        pool.TryUpdate(4, new AddUpdate(1));
        using (var write = pool.TryWrite(4))
        {
            Assert.IsFalse(write.Found);
        }

        pool.Remove(3);

        Assert.IsEmpty(changes);
    }

    [TestMethod]
    public void Multi_EveryWritePath_Notifies_PerInstance()
    {
        var (pool, changes) = Multi();

        pool.Add(2, new Value { Number = 1 });
        pool.Add(2, new Value { Number = 2 });
        pool.TryUpdateFirst(2, new NumberMatcher(1), new SetUpdate(10));
        var first = pool.GetFirstDenseIndex(2);
        pool.UpdateByDenseIndex(first, new AddUpdate(1));
        using (var write = pool.WriteByDenseIndex(first))
        {
            write.Value.Number = 20;
        }

        using (var write = pool.TryWriteFirst(2, new NumberMatcher(10)))
        {
            write.Value.Number = 30;
        }

        using (var write = pool.TryWriteFirst(2, new NumberMatcher(99)))
        {
            Assert.IsFalse(write.Found);
        }

        Assert.HasCount(6, changes, "Both Adds (not just the entity's first), TryUpdateFirst, UpdateByDenseIndex, WriteByDenseIndex, the matched TryWriteFirst.");
        Assert.IsTrue(changes.All(static c => c.EntityId == 2));
        Assert.AreEqual(1, changes[1].DenseIndex, "The second Add reports its own instance's dense index.");
    }

    [TestMethod]
    public void Multi_Removals_DoNotNotify()
    {
        var (pool, changes) = Multi();
        pool.Add(2, new Value { Number = 1 });
        pool.Add(2, new Value { Number = 2 });
        changes.Clear();

        pool.RemoveFirst(2, new NumberMatcher(1));
        pool.Remove(2);

        Assert.IsEmpty(changes);
    }

    private struct Timer : IScheduledTimer
    {
        public uint NextTickFrame { get; set; }
        public uint TimerWheelMark { get; set; }
    }

    [TestMethod]
    public void UnobservedPools_RefuseEverySubscriber()
    {
        var packed = new PackedComponentPool<Value>(16, 4, static (ref existing, incoming) => existing = incoming);
        var multi = new MultiComponentPool<Value>(16, 4);

        Assert.ThrowsExactly<InvalidOperationException>(() => packed.ComponentChanged += static (_, _) => { });
        Assert.ThrowsExactly<InvalidOperationException>(() => packed.ComponentRemoving += static (_, _) => { });
        Assert.ThrowsExactly<InvalidOperationException>(() => multi.ComponentChanged += static (_, _) => { });
        Assert.ThrowsExactly<InvalidOperationException>(() => multi.ComponentRemoving += static (_, _) => { });
    }

    [TestMethod]
    public void ScheduledTimerPools_AreObserved_WhateverTheyWereDeclared()
    {
        Assert.AreEqual(ComponentChangeObservation.Observed, new PackedComponentPool<Timer>(16, 4, static (ref existing, incoming) => existing = incoming).Observation);
        Assert.AreEqual(ComponentChangeObservation.Observed, new MultiComponentPool<Timer>(16, 4).Observation);
    }

    [TestMethod]
    public void Registration_CarriesTheDeclaredObservation()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterPackedPool<Value>(static (ref existing, incoming) => existing = incoming, observation: ComponentChangeObservation.Observed);
        manager.RegisterMultiPool<Timer>();

        Assert.AreEqual(ComponentChangeObservation.Observed, manager.GetPackedPool<Value>().Observation);
        Assert.AreEqual(ComponentChangeObservation.Observed, manager.GetMultiPool<Timer>().Observation);
    }
}
