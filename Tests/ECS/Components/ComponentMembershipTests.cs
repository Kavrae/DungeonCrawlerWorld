using Engine.ECS.Components;

namespace Tests.ECS.Components;

/// <summary>RemoveAllComponents visits only the pools an entity is held by, and index pages are freed once nothing in them is held.</summary>
[TestClass]
public sealed class ComponentMembershipTests
{
    private struct DirectTestComponent
    {
        public int Value;
    }

    private struct PackedTestComponent
    {
        public int Value;
    }

    private struct LaterPackedTestComponent
    {
        public int Value;
    }

    private struct MultiTestComponent
    {
        public int Value;
    }

    private struct NestedComponent<T>
    {
        public int Value;
    }

    private static ComponentManager CreateManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 64, initialComponentCapacity: 4);
        manager.RegisterDirectPool<DirectTestComponent>((ref existing, incoming) => existing = incoming);
        manager.RegisterPackedPool<PackedTestComponent>((ref existing, incoming) => existing = incoming, observation: ComponentChangeObservation.Observed);
        manager.RegisterMultiPool<MultiTestComponent>();
        manager.RegisterPackedPool<LaterPackedTestComponent>((ref existing, incoming) => existing = incoming);
        return manager;
    }

    [TestMethod]
    public void RemoveAllComponents_AfterComponentsComeAndGo_RemovesExactlyWhatTheEntityHolds()
    {
        var manager = CreateManager();
        var direct = manager.GetDirectPool<DirectTestComponent>();
        var packed = manager.GetPackedPool<PackedTestComponent>();
        var multi = manager.GetMultiPool<MultiTestComponent>();

        direct.Add(5, default);
        packed.Add(5, default);
        packed.Remove(5);
        multi.Add(5, default);
        multi.Add(5, default);
        packed.Add(6, default);

        manager.RemoveAllComponents(5);

        Assert.IsFalse(direct.Has(5));
        Assert.IsFalse(multi.Has(5));
        Assert.AreEqual(0, multi.Count);
        Assert.IsTrue(packed.Has(6), "Another entity's components are untouched.");
    }

    [TestMethod]
    public void RemoveAllComponents_EntityHoldingNothing_DoesNothing()
    {
        var manager = CreateManager();

        manager.RemoveAllComponents(3);
        manager.RemoveAllComponents(1_000_000);
    }

    [TestMethod]
    public void RemoveAllComponents_EntityPastTheManagersCapacity_IsStillRemoved()
    {
        var manager = CreateManager();
        var packed = manager.GetPackedPool<PackedTestComponent>();
        var multi = manager.GetMultiPool<MultiTestComponent>();

        packed.Add(5_000, default);
        multi.Add(5_000, default);

        manager.RemoveAllComponents(5_000);

        Assert.IsFalse(packed.Has(5_000));
        Assert.IsFalse(multi.Has(5_000));
    }

    /// <summary>What visiting every pool in registration order did: a removal handler that gives the entity a component in a later pool sees it removed in the same pass.</summary>
    [TestMethod]
    public void RemoveAllComponents_HandlerAddingToALaterPool_IsFollowed()
    {
        var manager = CreateManager();
        var packed = manager.GetPackedPool<PackedTestComponent>();
        var later = manager.GetPackedPool<LaterPackedTestComponent>();
        packed.ComponentRemoving += (entityId, _) => later.Add(entityId, default);
        packed.Add(9, default);

        manager.RemoveAllComponents(9);

        Assert.IsFalse(packed.Has(9));
        Assert.IsFalse(later.Has(9));
    }

    /// <summary>Past 64 pools every entity's mask spans a second word, including pools registered after components were added.</summary>
    [TestMethod]
    public void RemoveAllComponents_MoreThanSixtyFourPools_ReachesEveryWord()
    {
        var manager = new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 4);
        manager.RegisterPackedPool<PackedTestComponent>((ref existing, incoming) => existing = incoming);
        var first = manager.GetPackedPool<PackedTestComponent>();
        first.Add(2, default);

        var registerMultiPool = typeof(ComponentManager).GetMethod(nameof(ComponentManager.RegisterMultiPool))!;
        var nestedType = typeof(int);
        for (var poolIndex = 0; poolIndex < 70; poolIndex++)
        {
            nestedType = typeof(NestedComponent<>).MakeGenericType(nestedType);
            registerMultiPool.MakeGenericMethod(nestedType).Invoke(manager, [null, ComponentChangeObservation.Unobserved]);
        }

        manager.RegisterPackedPool<LaterPackedTestComponent>((ref existing, incoming) => existing = incoming);
        var last = manager.GetPackedPool<LaterPackedTestComponent>();
        last.Add(2, default);
        Assert.IsGreaterThan(64, manager.PoolsInRegistrationOrder.Count);

        manager.RemoveAllComponents(2);

        Assert.IsFalse(first.Has(2));
        Assert.IsFalse(last.Has(2));
    }

    /// <summary>Holders moving on to other id ranges -- what recycled ids do over a long session -- reuse freed pages, so the table stays at the most pages ever in use at once.</summary>
    [TestMethod]
    public void PackedPool_HoldersMovingAcrossIdRanges_NeverGrowPastTheMostPagesInUseAtOnce()
    {
        var packed = new Engine.ECS.Components.Stores.PackedComponentPool<PackedTestComponent>(entityCapacity: 100 * 1024, initialCapacity: 32, (ref existing, incoming) => existing = incoming);
        const int pagesInUseAtOnce = 5;
        var pageBytes = 1024 * sizeof(int);

        var holders = new List<int>();
        for (var page = 0; page < pagesInUseAtOnce; page++)
        {
            holders.Add(page * 1024);
            packed.Add(page * 1024, default);
        }

        var bytesAtPeak = packed.EstimatedBytes;

        for (var round = 1; round < 19; round++)
        {
            for (var page = 0; page < pagesInUseAtOnce; page++)
            {
                packed.Remove(holders[page]);
                holders[page] = (round * pagesInUseAtOnce + page) * 1024 + round;
                packed.Add(holders[page], default);
            }
        }

        Assert.AreEqual(bytesAtPeak, packed.EstimatedBytes, "Every page after the first five came from a freed one.");
        Assert.AreEqual(pagesInUseAtOnce, packed.Count);

        packed.Add(0, default);
        Assert.AreEqual(bytesAtPeak + pageBytes, packed.EstimatedBytes, "A sixth page in use at once is the first new one.");
    }

    [TestMethod]
    public void PackedPool_ReusedPage_ReadsEmptyApartFromItsNewHolder()
    {
        var packed = new Engine.ECS.Components.Stores.PackedComponentPool<PackedTestComponent>(entityCapacity: 20_000, initialCapacity: 32, (ref existing, incoming) => existing = incoming);
        packed.Add(3, default);
        packed.Add(700, default);
        packed.Remove(3);
        packed.Remove(700);

        packed.Add(15 * 1024 + 3, default);

        Assert.IsTrue(packed.Has(15 * 1024 + 3));
        Assert.IsFalse(packed.Has(15 * 1024 + 700));
        Assert.IsFalse(packed.Has(3));
        Assert.IsFalse(packed.Has(700));
    }

    [TestMethod]
    public void MultiPool_LastInstanceLeaving_FreesThePage_AndAPageStaysWhileAnyEntityInItHoldsOne()
    {
        var multi = new Engine.ECS.Components.Stores.MultiComponentPool<MultiTestComponent>(entityCapacity: 20_000, initialCapacity: 32);
        var emptyBytes = multi.EstimatedBytes;
        var chainPageBytes = 1024 * 12;

        multi.Add(1, default);
        multi.Add(2, default);
        multi.Add(2, default);
        multi.Remove(1);
        Assert.AreEqual(emptyBytes + chainPageBytes, multi.EstimatedBytes, "Entity 2 still holds the page.");
        Assert.AreEqual(2, multi.CountForEntity(2));

        multi.Remove(2);
        Assert.AreEqual(emptyBytes + chainPageBytes, multi.EstimatedBytes, "The freed page is kept as a spare.");

        multi.Add(5_000, default);
        Assert.AreEqual(emptyBytes + chainPageBytes, multi.EstimatedBytes, "And taken again for the next page.");
        Assert.AreEqual(0, multi.CountForEntity(2));
        Assert.AreEqual(0u, multi.GetEntityVersion(2));
        Assert.AreEqual(1, multi.CountForEntity(5_000));
    }
}
