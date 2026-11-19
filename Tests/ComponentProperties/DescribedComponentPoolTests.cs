using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Properties;

namespace Tests.ComponentProperties;

/// <summary>A test component the generator describes, for the pools' table reads and writes.</summary>
[DescribedComponent]
public partial struct DescribedPoolTestComponent
{
    public int Value { get; set; }

    [ReadOnlyProperty]
    public int Locked { get; set; }
}

/// <summary>Each pool kind reads every instance an entity holds through its table, with its version, and writes one value through a write scope only when the table takes it.</summary>
[TestClass]
public sealed class DescribedComponentPoolTests
{
    private static MergeAction<DescribedPoolTestComponent> Replace => static (ref existing, incoming) => existing = incoming;

    public static IEnumerable<object[]> SingleInstancePools() =>
    [
        [new DirectComponentPool<DescribedPoolTestComponent>(8, Replace)],
        [new PackedComponentPool<DescribedPoolTestComponent>(8, 4, Replace)],
    ];

    [TestMethod]
    [DynamicData(nameof(SingleInstancePools))]
    public void ASingleInstancePool_ReadsTheInstanceAndVersionsAnAcceptedWriteOnly(IDescribedComponentPool pool)
    {
        Add(pool, 2, new DescribedPoolTestComponent { Value = 3, Locked = 4 });
        var before = new RecordingVisitor();

        Assert.AreEqual(1, pool.ReadProperties(2, before));
        Assert.IsTrue(pool.WriteProperty(2, 0, [0], ComponentPropertyValue.FromInteger(7), out _));
        Assert.IsFalse(pool.WriteProperty(2, 0, [1], ComponentPropertyValue.FromInteger(7), out _), "Locked is read-only");
        Assert.IsFalse(pool.WriteProperty(2, 1, [0], ComponentPropertyValue.FromInteger(7), out _), "one instance only");
        Assert.IsFalse(pool.WriteProperty(3, 0, [0], ComponentPropertyValue.FromInteger(7), out _), "an entity without one");
        Assert.AreEqual(0, pool.ReadProperties(3, new RecordingVisitor()));

        var after = new RecordingVisitor();
        pool.ReadProperties(2, after);
        Assert.AreEqual(7, after.Leaves[0].Value.Integer);
        Assert.AreEqual(4, after.Leaves[1].Value.Integer);
        Assert.AreEqual(before.InstanceVersions[0] + 1, after.InstanceVersions[0], "one version for the accepted write, none for the refusals");
    }

    [TestMethod]
    public void AMultiPool_ReadsEachInstanceAndWritesTheOneNamed()
    {
        var pool = new MultiComponentPool<DescribedPoolTestComponent>(entityCapacity: 8, initialCapacity: 4);
        pool.Add(1, new DescribedPoolTestComponent { Value = 10 });
        pool.Add(1, new DescribedPoolTestComponent { Value = 20 });
        var before = new RecordingVisitor();

        Assert.AreEqual(2, pool.ReadProperties(1, before));
        var twenty = before.Leaves.Single(leaf => leaf.Path[0] == 0 && leaf.Value.Integer == 20).Instance;
        Assert.IsTrue(pool.WriteProperty(1, twenty, [0], ComponentPropertyValue.FromInteger(21), out _));
        Assert.IsFalse(pool.WriteProperty(1, 2, [0], ComponentPropertyValue.FromInteger(21), out _));

        var after = new RecordingVisitor();
        pool.ReadProperties(1, after);
        CollectionAssert.AreEquivalent(new long[] { 10, 21 }, after.Leaves.Where(leaf => leaf.Path[0] == 0).Select(leaf => leaf.Value.Integer).ToArray());
        Assert.AreEqual(before.InstanceVersions[1 - twenty], after.InstanceVersions[1 - twenty]);
        Assert.AreEqual(before.InstanceVersions[twenty] + 1, after.InstanceVersions[twenty]);
    }

    [TestMethod]
    public void AnUndescribedComponentsPool_HasNoTableAndReadsNothing()
    {
        var pool = new PackedComponentPool<int>(8, 4, static (ref existing, incoming) => existing = incoming);
        pool.Add(0, 5);

        Assert.IsNull(pool.PropertyTable);
        Assert.AreEqual(0, pool.ReadProperties(0, new RecordingVisitor()));
        Assert.IsFalse(pool.WriteProperty(0, 0, [0], ComponentPropertyValue.FromInteger(1), out _));
    }

    private static void Add(IDescribedComponentPool pool, int entityId, DescribedPoolTestComponent component)
    {
        switch (pool)
        {
            case DirectComponentPool<DescribedPoolTestComponent> direct:
                direct.Add(entityId, component);
                break;
            case PackedComponentPool<DescribedPoolTestComponent> packed:
                packed.Add(entityId, component);
                break;
        }
    }
}
