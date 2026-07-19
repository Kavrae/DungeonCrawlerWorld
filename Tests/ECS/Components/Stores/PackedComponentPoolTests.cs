using Engine.ECS.Components.Stores;

namespace Tests.ECS.Components.Stores;

[TestClass]
public sealed class PackedComponentPoolTests
{
    private struct TestComponent
    {
        public int Value;
    }

    private static Engine.ECS.Components.MergeAction<TestComponent> AverageMerge =>
        (ref TestComponent existing, TestComponent incoming) => existing.Value = (existing.Value + incoming.Value) / 2;

    [TestMethod]
    public void Add_PacksIntoDenseStorageStartingAtZero()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 2, AverageMerge);

        pool.Add(7, new TestComponent { Value = 1 });
        pool.Add(3, new TestComponent { Value = 2 });

        Assert.AreEqual(2, pool.Count);
        Assert.AreEqual(7, pool.GetEntityIdByDenseIndex(0));
        Assert.AreEqual(3, pool.GetEntityIdByDenseIndex(1));
    }

    [TestMethod]
    public void Add_BeyondInitialCapacity_GrowsDenseStorage()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 2, AverageMerge);

        pool.Add(0, new TestComponent());
        pool.Add(1, new TestComponent());
        pool.Add(2, new TestComponent());

        Assert.AreEqual(3, pool.Count);
        Assert.IsTrue(pool.Has(2));
    }

    [TestMethod]
    public void Remove_MiddleEntry_SwapsLastEntryIntoItsSlot()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4, AverageMerge);
        pool.Add(0, new TestComponent { Value = 10 });
        pool.Add(1, new TestComponent { Value = 20 });
        pool.Add(2, new TestComponent { Value = 30 });

        var removed = pool.Remove(0);

        Assert.IsTrue(removed);
        Assert.AreEqual(2, pool.Count);
        Assert.IsFalse(pool.Has(0));
        // Entity 2 (previously the last dense entry) should have been swapped into slot 0.
        Assert.AreEqual(30, pool.GetReadonly(2).Value);
        Assert.IsTrue(pool.Has(1));
        Assert.IsTrue(pool.Has(2));
    }

    [TestMethod]
    public void UpdateByDenseIndex_MutatesAndBumpsVersion()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4, AverageMerge);
        pool.Add(5, new TestComponent { Value = 1 });

        pool.UpdateByDenseIndex(0, static (ref TestComponent c) => c.Value += 100);

        Assert.AreEqual(101, pool.GetByDenseIndex(0).Value);
        Assert.AreEqual(2u, pool.GetVersionByDenseIndex(0));
    }

    [TestMethod]
    public void Merge_ExistingComponent_AveragesValue()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4, AverageMerge);
        pool.Add(0, new TestComponent { Value = 40 });

        pool.Merge(0, new TestComponent { Value = 60 });

        Assert.AreEqual(50, pool.GetReadonly(0).Value);
    }

    [TestMethod]
    public void Resize_OnlyGrowsSparseMap_NotDenseStorage()
    {
        var pool = new PackedComponentPool<TestComponent>(maximumEntityCount: 4, initialCapacity: 4, AverageMerge);

        pool.Resize(100);
        pool.Add(99, new TestComponent { Value = 1 });

        Assert.IsTrue(pool.Has(99));
    }
}
