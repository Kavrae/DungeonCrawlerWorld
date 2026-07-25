namespace Tests.ECS.Components.Stores;

using Engine.ECS.Components.Stores;

[TestClass]
public sealed class MultiComponentPoolTests
{
    private struct TestComponent
    {
        public int Value;
    }

    [TestMethod]
    public void Add_MultipleForSameEntity_AllCountedUnderThatEntity()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);

        pool.Add(3, new TestComponent { Value = 1 });
        pool.Add(3, new TestComponent { Value = 2 });
        pool.Add(3, new TestComponent { Value = 3 });

        Assert.AreEqual(3, pool.CountForEntity(3));
        Assert.AreEqual(3, pool.Count);
        Assert.IsTrue(pool.Has(3));
    }

    [TestMethod]
    public void GetFirstDenseIndex_WalkChain_VisitsAllComponentsForEntity()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(0, new TestComponent { Value = 2 });

        var values = new List<int>();
        for (var i = pool.GetFirstDenseIndex(0); i != -1; i = pool.GetNextDenseIndex(i))
        {
            values.Add(pool.GetReadonlyByDenseIndex(i).Value);
        }

        Assert.HasCount(2, values);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, values);
    }

    [TestMethod]
    public void Remove_RemovesAllComponentsForEntity()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(0, new TestComponent { Value = 2 });
        pool.Add(1, new TestComponent { Value = 3 });

        var removed = pool.Remove(0);

        Assert.IsTrue(removed);
        Assert.AreEqual(0, pool.CountForEntity(0));
        Assert.IsFalse(pool.Has(0));
        Assert.AreEqual(1, pool.Count);
        Assert.IsTrue(pool.Has(1));
    }

    [TestMethod]
    public void RemoveFirst_MatchingPredicate_RemovesOnlyThatEntry()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(0, new TestComponent { Value = 2 });

        var removed = pool.RemoveFirst(0, (ref readonly c) => c.Value == 1);

        Assert.IsTrue(removed);
        Assert.AreEqual(1, pool.CountForEntity(0));
    }

    [TestMethod]
    public void SwapRemove_RepairsLinkedChainForMovedEntry()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 8);
        // Entity 0 gets three components; entity 1 gets one, deliberately interleaved so a
        // swap-remove has to repair a chain belonging to whichever entity owned the moved slot.
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(1, new TestComponent { Value = 100 });
        pool.Add(0, new TestComponent { Value = 2 });
        pool.Add(0, new TestComponent { Value = 3 });

        pool.RemoveFirst(0, (ref readonly c) => c.Value == 1);

        Assert.AreEqual(2, pool.CountForEntity(0));
        Assert.AreEqual(1, pool.CountForEntity(1));

        var entity0Values = new List<int>();
        for (var i = pool.GetFirstDenseIndex(0); i != -1; i = pool.GetNextDenseIndex(i))
        {
            entity0Values.Add(pool.GetReadonlyByDenseIndex(i).Value);
        }
        CollectionAssert.AreEquivalent(new[] { 2, 3 }, entity0Values);

        var entity1Index = pool.GetFirstDenseIndex(1);
        Assert.AreEqual(100, pool.GetReadonlyByDenseIndex(entity1Index).Value);
    }

    [TestMethod]
    public void EntityVersion_IncrementsOnAddAndRemove()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);

        pool.Add(0, new TestComponent { Value = 1 });
        var versionAfterAdd = pool.GetEntityVersion(0);

        pool.Remove(0);
        var versionAfterRemove = pool.GetEntityVersion(0);

        Assert.IsGreaterThan(versionAfterAdd, versionAfterRemove);
    }

    [TestMethod]
    public void CopyInspectionDataForEntity_SingleInstance_OneRowWithNoCountSuffix()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 1 });
        var destination = new List<Engine.ECS.Components.InspectedComponentEntry>();

        var rowsAdded = pool.CopyInspectionDataForEntity(0, destination);

        Assert.AreEqual(1, rowsAdded);
        Assert.HasCount(1, destination);
        Assert.DoesNotContain("x1)", destination[0].Value);
    }

    /// <summary>Regression test for the Selection window grouping feature: several value-equal instances (e.g. several StatusEffectStack entries with the same EffectType/Source) collapse into one row with a count, instead of one near-duplicate row per instance.</summary>
    [TestMethod]
    public void CopyInspectionDataForEntity_MultipleIdenticalInstances_GroupsIntoOneRowWithCount()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 7 });
        pool.Add(0, new TestComponent { Value = 7 });
        pool.Add(0, new TestComponent { Value = 7 });
        var destination = new List<Engine.ECS.Components.InspectedComponentEntry>();

        var rowsAdded = pool.CopyInspectionDataForEntity(0, destination);

        Assert.AreEqual(1, rowsAdded);
        Assert.HasCount(1, destination);
        StringAssert.Contains(destination[0].Value, "(x3)");
    }

    [TestMethod]
    public void CopyInspectionDataForEntity_MixOfDistinctAndDuplicateInstances_EachDistinctValueGetsItsOwnRow()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 8);
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(0, new TestComponent { Value = 2 });
        pool.Add(0, new TestComponent { Value = 1 });
        pool.Add(0, new TestComponent { Value = 2 });
        pool.Add(0, new TestComponent { Value = 2 });
        var destination = new List<Engine.ECS.Components.InspectedComponentEntry>();

        var rowsAdded = pool.CopyInspectionDataForEntity(0, destination);

        Assert.AreEqual(2, rowsAdded);
        Assert.HasCount(2, destination);
        Assert.IsTrue(destination.Exists(entry => entry.Value.Contains("(x2)")), "The two Value=1 instances should collapse into one row counted x2.");
        Assert.IsTrue(destination.Exists(entry => entry.Value.Contains("(x3)")), "The three Value=2 instances should collapse into one row counted x3.");
    }

    [TestMethod]
    public void CopyInspectionDataForEntity_GroupedRow_UsesTheHighestVersionInTheGroup()
    {
        var pool = new MultiComponentPool<TestComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new TestComponent { Value = 5 }); // dense index 0, version 1
        pool.Add(0, new TestComponent { Value = 5 }); // dense index 1, version 1
        pool.UpdateByDenseIndex(pool.GetFirstDenseIndex(0), static (ref TestComponent c) => { }); // bumps whichever instance is first in the chain to version 2
        var destination = new List<Engine.ECS.Components.InspectedComponentEntry>();

        pool.CopyInspectionDataForEntity(0, destination);

        Assert.AreEqual(2u, destination[0].Version);
    }
}