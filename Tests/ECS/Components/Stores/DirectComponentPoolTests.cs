using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Tests.ECS.Components.Stores;

[TestClass]
public sealed class DirectComponentPoolTests
{
    private struct TestComponent
    {
        public int Value;
    }

    private static MergeAction<TestComponent> AverageMerge => (ref TestComponent existing, TestComponent incoming) =>
    {
        existing.Value = (existing.Value + incoming.Value) / 2;
    };

    [TestMethod]
    public void NewPool_HasNoComponents()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        Assert.AreEqual(0, pool.Count);
        Assert.AreEqual(5, pool.Capacity);
        Assert.IsFalse(pool.Has(0));
    }

    [TestMethod]
    public void Has_OutOfRangeEntityId_Throws()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pool.Has(5));
    }

    [TestMethod]
    public void Add_ThenGet_ReturnsStoredValue()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        pool.Add(2, new TestComponent { Value = 42 });

        Assert.IsTrue(pool.Has(2));
        Assert.AreEqual(1, pool.Count);
        Assert.AreEqual(42, pool.GetReadonly(2).Value);
        Assert.AreEqual(1u, pool.GetVersion(2));
    }

    [TestMethod]
    public void Add_AlreadyPresent_Throws()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);
        pool.Add(0, new TestComponent { Value = 1 });

        Assert.ThrowsExactly<InvalidOperationException>(() => pool.Add(0, new TestComponent { Value = 2 }));
    }

    [TestMethod]
    public void Merge_ExistingComponent_AppliesMergeActionAndBumpsVersion()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);
        pool.Add(0, new TestComponent { Value = 100 });

        pool.Merge(0, new TestComponent { Value = 200 });

        Assert.AreEqual(150, pool.GetReadonly(0).Value);
        Assert.AreEqual(2u, pool.GetVersion(0));
    }

    [TestMethod]
    public void Merge_NoExistingComponent_AddsInstead()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        pool.Merge(0, new TestComponent { Value = 7 });

        Assert.IsTrue(pool.Has(0));
        Assert.AreEqual(7, pool.GetReadonly(0).Value);
    }

    [TestMethod]
    public void TryUpdate_MutatesInPlaceAndBumpsVersion()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);
        pool.Add(0, new TestComponent { Value = 1 });

        var updated = pool.TryUpdate(0, static (ref TestComponent c) => c.Value += 10);

        Assert.IsTrue(updated);
        Assert.AreEqual(11, pool.GetReadonly(0).Value);
        Assert.AreEqual(2u, pool.GetVersion(0));
    }

    [TestMethod]
    public void TryUpdate_NotPresent_ReturnsFalse()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        var updated = pool.TryUpdate(0, static (ref TestComponent c) => c.Value += 10);

        Assert.IsFalse(updated);
    }

    [TestMethod]
    public void Remove_PresentComponent_ClearsSlotAndDecrementsCount()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);
        pool.Add(1, new TestComponent { Value = 5 });

        var removed = pool.Remove(1);

        Assert.IsTrue(removed);
        Assert.AreEqual(0, pool.Count);
        Assert.IsFalse(pool.Has(1));
    }

    [TestMethod]
    public void Remove_NotPresent_ReturnsFalse()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);

        Assert.IsFalse(pool.Remove(1));
    }

    [TestMethod]
    public void Resize_GrowsArraysAndPreservesExistingComponents()
    {
        var pool = new DirectComponentPool<TestComponent>(2, AverageMerge);
        pool.Add(0, new TestComponent { Value = 9 });

        pool.Resize(4);

        Assert.AreEqual(4, pool.Capacity);
        Assert.IsTrue(pool.Has(0));
        Assert.AreEqual(9, pool.GetReadonly(0).Value);
        Assert.IsFalse(pool.Has(3));
    }

    [TestMethod]
    public void CopyInspectionDataForEntity_PresentComponent_AddsOneEntry()
    {
        var pool = new DirectComponentPool<TestComponent>(5, AverageMerge);
        pool.Add(0, new TestComponent { Value = 3 });
        var destination = new List<InspectedComponentEntry>();

        var copied = pool.CopyInspectionDataForEntity(0, destination);

        Assert.AreEqual(1, copied);
        Assert.HasCount(1, destination);
        Assert.AreEqual(ComponentPoolType.Direct, destination[0].ComponentPoolType);
    }
}
