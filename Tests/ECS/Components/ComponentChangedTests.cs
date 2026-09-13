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

    private static (PackedComponentPool<Value> Pool, List<(int EntityId, int DenseIndex)> Changes) Packed()
    {
        var pool = new PackedComponentPool<Value>(16, 4, static (ref existing, incoming) => existing.Number += incoming.Number);
        var changes = new List<(int, int)>();
        pool.ComponentChanged += (entityId, denseIndex) => changes.Add((entityId, denseIndex));
        return (pool, changes);
    }

    private static (MultiComponentPool<Value> Pool, List<(int EntityId, int DenseIndex)> Changes) Multi()
    {
        var pool = new MultiComponentPool<Value>(16, 4);
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
        pool.TryUpdate(3, static (ref Value v) => v.Number++);
        pool.TryUpdate(3, 2, static (ref Value v, int add) => v.Number += add);
        var denseIndex = pool.GetDenseIndex(3);
        pool.SetByDenseIndex(denseIndex, new Value { Number = 1 });
        pool.UpdateByDenseIndex(denseIndex, static (ref Value v) => v.Number++);
        pool.UpdateByDenseIndex(denseIndex, 2, static (ref Value v, int add) => v.Number += add);
        pool.IncrementVersionByDenseIndex(denseIndex);

        CollectionAssert.AreEqual(
            new[] { (3, 0), (3, 0), (5, 1), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0), (3, 0) },
            changes,
            "Add, Merge, Merge-as-Add, TrySet, TryUpdate x2, SetByDenseIndex, UpdateByDenseIndex x2, IncrementVersionByDenseIndex.");
    }

    [TestMethod]
    public void Packed_RemoveAndFailedWrites_DoNotNotify()
    {
        var (pool, changes) = Packed();
        pool.Add(3, new Value());
        changes.Clear();

        pool.TrySet(4, new Value());
        pool.TryUpdate(4, static (ref Value v) => v.Number++);
        pool.Remove(3);

        Assert.IsEmpty(changes);
    }

    [TestMethod]
    public void Multi_EveryWritePath_Notifies_PerInstance()
    {
        var (pool, changes) = Multi();

        pool.Add(2, new Value { Number = 1 });
        pool.Add(2, new Value { Number = 2 });
        pool.TryUpdateFirst(2, static (ref readonly Value v) => v.Number == 1, static (ref Value v) => v.Number = 10);
        pool.TryUpdateFirst(2, 2, static (ref readonly Value v, int n) => v.Number == n, static (ref Value v, int n) => v.Number = n * 10);
        var first = pool.GetFirstDenseIndex(2);
        pool.UpdateByDenseIndex(first, static (ref Value v) => v.Number++);
        pool.UpdateByDenseIndex(first, 1, static (ref Value v, int add) => v.Number += add);
        pool.IncrementVersionByDenseIndex(first);

        Assert.HasCount(7, changes, "Both Adds (not just the entity's first), TryUpdateFirst x2, UpdateByDenseIndex x2, IncrementVersionByDenseIndex.");
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

        pool.RemoveFirst(2, static (ref readonly Value v) => v.Number == 1);
        pool.Remove(2);

        Assert.IsEmpty(changes);
    }
}
