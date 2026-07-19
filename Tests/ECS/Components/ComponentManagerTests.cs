using Engine.ECS.Components;

namespace Tests.ECS.Components;

[TestClass]
public sealed class ComponentManagerTests
{
    private struct DirectTestComponent
    {
        public int Value;
    }

    private struct PackedTestComponent
    {
        public int Value;
    }

    private struct MultiTestComponent
    {
        public int Value;
    }

    [TestMethod]
    public void RegisterDirectPool_ThenGetDirectPool_ReturnsSamePool()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);

        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);

        Assert.IsTrue(manager.IsRegistered<DirectTestComponent>());
        Assert.AreEqual(ComponentPoolType.Direct, manager.GetPoolType<DirectTestComponent>());

        var pool = manager.GetDirectPool<DirectTestComponent>();
        pool.Add(0, new DirectTestComponent { Value = 5 });
        Assert.AreEqual(5, manager.GetDirectPool<DirectTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void RegisterSameTypeTwice_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming));
    }

    [TestMethod]
    public void GetPackedPool_WrongPoolKind_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.GetPackedPool<DirectTestComponent>());
    }

    [TestMethod]
    public void GetPool_Unregistered_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.GetDirectPool<DirectTestComponent>());
    }

    [TestMethod]
    public void ResizeEntityCapacity_ResizesEveryRegisteredPool()
    {
        var manager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);
        manager.RegisterPackedPool<PackedTestComponent>((ref PackedTestComponent existing, PackedTestComponent incoming) => existing = incoming);
        manager.RegisterMultiPool<MultiTestComponent>();

        manager.ResizeEntityCapacity(50);

        Assert.AreEqual(50, manager.GetDirectPool<DirectTestComponent>().Capacity);
        // Packed/Multi pools accept entity ids up to the new capacity without throwing.
        manager.GetPackedPool<PackedTestComponent>().Add(49, new PackedTestComponent { Value = 1 });
        manager.GetMultiPool<MultiTestComponent>().Add(49, new MultiTestComponent { Value = 1 });
    }

    [TestMethod]
    public void RemoveAllComponents_RemovesAcrossAllPoolKinds()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);
        manager.RegisterPackedPool<PackedTestComponent>((ref PackedTestComponent existing, PackedTestComponent incoming) => existing = incoming);
        manager.RegisterMultiPool<MultiTestComponent>();

        manager.GetDirectPool<DirectTestComponent>().Add(0, new DirectTestComponent());
        manager.GetPackedPool<PackedTestComponent>().Add(0, new PackedTestComponent());
        manager.GetMultiPool<MultiTestComponent>().Add(0, new MultiTestComponent());
        manager.GetMultiPool<MultiTestComponent>().Add(0, new MultiTestComponent());

        manager.RemoveAllComponents(0);

        Assert.IsFalse(manager.GetDirectPool<DirectTestComponent>().Has(0));
        Assert.IsFalse(manager.GetPackedPool<PackedTestComponent>().Has(0));
        Assert.IsFalse(manager.GetMultiPool<MultiTestComponent>().Has(0));
    }

    /// <summary>
    /// Merge&lt;T&gt; lets a caller (e.g. a blueprint) add or merge a component without knowing
    /// which pool kind T was registered as -- these four tests cover the dispatch to each
    /// kind, using a merge lambda that combines values (rather than overwriting) so a
    /// passing test actually proves Merge, not just Add, was called underneath.
    /// </summary>
    [TestMethod]
    public void Merge_DirectPool_AbsentComponent_AddsIt()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing.Value += incoming.Value);

        manager.Merge(0, new DirectTestComponent { Value = 5 });

        Assert.AreEqual(5, manager.GetDirectPool<DirectTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void Merge_DirectPool_ExistingComponent_CombinesViaMergeAction()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing.Value += incoming.Value);
        manager.Merge(0, new DirectTestComponent { Value = 5 });

        manager.Merge(0, new DirectTestComponent { Value = 3 });

        Assert.AreEqual(8, manager.GetDirectPool<DirectTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void Merge_PackedPool_ExistingComponent_CombinesViaMergeAction()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterPackedPool<PackedTestComponent>((ref PackedTestComponent existing, PackedTestComponent incoming) => existing.Value += incoming.Value);
        manager.Merge(0, new PackedTestComponent { Value = 5 });

        manager.Merge(0, new PackedTestComponent { Value = 3 });

        Assert.AreEqual(8, manager.GetPackedPool<PackedTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void Merge_MultiPool_AlwaysAddsRatherThanCombining()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterMultiPool<MultiTestComponent>();

        manager.Merge(0, new MultiTestComponent { Value = 5 });
        manager.Merge(0, new MultiTestComponent { Value = 3 });

        var pool = manager.GetMultiPool<MultiTestComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));
    }

    [TestMethod]
    public void Merge_UnregisteredType_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.Merge(0, new DirectTestComponent { Value = 1 }));
    }

    /// <summary>
    /// TryUpdate&lt;T&gt; mirrors Merge&lt;T&gt;'s "caller doesn't need to know the pool kind"
    /// dispatch for Direct and Packed pools. Multi pools are deliberately unsupported (an
    /// entity may have 0..N components of that type, so there's no single "the" component to
    /// update in place) -- see the last test below.
    /// </summary>
    [TestMethod]
    public void TryUpdate_DirectPool_ExistingComponent_MutatesInPlaceAndReturnsTrue()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);
        manager.Merge(0, new DirectTestComponent { Value = 5 });

        var updated = manager.TryUpdate(0, (ref DirectTestComponent c) => c.Value += 10);

        Assert.IsTrue(updated);
        Assert.AreEqual(15, manager.GetDirectPool<DirectTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void TryUpdate_DirectPool_AbsentComponent_ReturnsFalse()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref DirectTestComponent existing, DirectTestComponent incoming) => existing = incoming);

        var updated = manager.TryUpdate(0, (ref DirectTestComponent c) => c.Value += 10);

        Assert.IsFalse(updated);
    }

    [TestMethod]
    public void TryUpdate_PackedPool_ExistingComponent_MutatesInPlaceAndReturnsTrue()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterPackedPool<PackedTestComponent>((ref PackedTestComponent existing, PackedTestComponent incoming) => existing = incoming);
        manager.Merge(0, new PackedTestComponent { Value = 5 });

        var updated = manager.TryUpdate(0, (ref PackedTestComponent c) => c.Value += 10);

        Assert.IsTrue(updated);
        Assert.AreEqual(15, manager.GetPackedPool<PackedTestComponent>().GetReadonly(0).Value);
    }

    [TestMethod]
    public void TryUpdate_MultiPool_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        manager.RegisterMultiPool<MultiTestComponent>();
        manager.Merge(0, new MultiTestComponent { Value = 5 });

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.TryUpdate(0, (ref MultiTestComponent c) => c.Value += 10));
    }

    [TestMethod]
    public void TryUpdate_UnregisteredType_Throws()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.TryUpdate(0, (ref DirectTestComponent c) => c.Value += 1));
    }
}
