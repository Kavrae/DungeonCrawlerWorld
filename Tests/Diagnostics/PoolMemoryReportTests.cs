using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Tests.Diagnostics;

[TestClass]
public sealed class PoolMemoryReportTests
{
    private struct Counter
    {
        public int Value;
    }

    private struct Named(string name)
    {
        public string Name = name;
    }

    private struct Tag
    {
        public int Value;
    }

    private static (ComponentManager Components, EntityManager Entities) CreateWorld()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 4);
        componentManager.RegisterPackedPool<Counter>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<Named>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<Tag>();
        return (componentManager, new EntityManager(componentManager, 16));
    }

    private static PoolMemoryReportEntry EntryFor<T>(IReadOnlyList<PoolMemoryReportEntry> entries) =>
        entries.Single(static entry => entry.ComponentType == typeof(T).Name);

    [TestMethod]
    public void Complete_CountsDistinctValuesAtBaselineAndHoldersWhoseValueChanged()
    {
        var (components, entities) = CreateWorld();
        var counters = components.GetPackedPool<Counter>();
        var ids = Enumerable.Range(0, 3).Select(_ => entities.CreateEntity()).ToArray();
        counters.Add(ids[0], new Counter { Value = 1 });
        counters.Add(ids[1], new Counter { Value = 1 });
        counters.Add(ids[2], new Counter { Value = 2 });

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        counters.TrySet(ids[2], new Counter { Value = 3 });

        var entry = EntryFor<Counter>(report.Complete());
        Assert.AreEqual("Packed", entry.PoolKind);
        Assert.AreEqual(2, entry.DistinctValues);
        Assert.AreEqual(3, entry.HoldersAtStart);
        Assert.AreEqual(3, entry.SurvivingHolders);
        Assert.AreEqual(1, entry.ChangedHolders);
    }

    [TestMethod]
    public void Complete_LeavesOutDestroyedHoldersEvenWhenTheirIdIsReused()
    {
        var (components, entities) = CreateWorld();
        var counters = components.GetPackedPool<Counter>();
        var kept = entities.CreateEntity();
        var destroyed = entities.CreateEntity();
        counters.Add(kept, new Counter { Value = 1 });
        counters.Add(destroyed, new Counter { Value = 1 });

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        entities.DestroyEntity(destroyed);
        var reused = entities.CreateEntity();
        Assert.AreEqual(destroyed, reused);
        counters.Add(reused, new Counter { Value = 1 });

        var entry = EntryFor<Counter>(report.Complete());
        Assert.AreEqual(2, entry.HoldersAtStart);
        Assert.AreEqual(1, entry.SurvivingHolders);
        Assert.AreEqual(0, entry.ChangedHolders);
    }

    [TestMethod]
    public void Complete_TreatsAMultiPoolEntitysValuesAsAMultiset()
    {
        var (components, entities) = CreateWorld();
        var tags = components.GetMultiPool<Tag>();
        var reordered = entities.CreateEntity();
        var grown = entities.CreateEntity();
        tags.Add(reordered, new Tag { Value = 1 });
        tags.Add(reordered, new Tag { Value = 2 });
        tags.Add(grown, new Tag { Value = 1 });

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        tags.RemoveFirst(reordered, static (ref readonly tag) => tag.Value == 1);
        tags.Add(reordered, new Tag { Value = 1 });
        tags.Add(grown, new Tag { Value = 1 });

        var entry = EntryFor<Tag>(report.Complete());
        Assert.AreEqual("Multi", entry.PoolKind);
        Assert.AreEqual(2, entry.SurvivingHolders);
        Assert.AreEqual(1, entry.ChangedHolders);
    }

    [TestMethod]
    public void Complete_ComparesReferenceHoldingComponentsByFieldValue()
    {
        var (components, entities) = CreateWorld();
        var names = components.GetDirectPool<Named>();
        var first = entities.CreateEntity();
        var second = entities.CreateEntity();
        names.Add(first, new Named("Goblin"));
        names.Add(second, new Named(new string("Goblin".ToCharArray())));

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        names.TrySet(first, new Named(new string("Goblin".ToCharArray())));

        var entry = EntryFor<Named>(report.Complete());
        Assert.AreEqual("Direct", entry.PoolKind);
        Assert.IsTrue(entry.HoldsReferences);
        Assert.AreEqual(1, entry.DistinctValues);
        Assert.AreEqual(0, entry.ChangedHolders);
    }

    [TestMethod]
    public void Complete_IgnoresPaddingBytesThatDifferBetweenEqualValues()
    {
        var (components, entities) = CreateWorld();
        components.RegisterPackedPool<Padded>(static (ref existing, incoming) => existing = incoming);
        var padded = components.GetPackedPool<Padded>();
        var entityId = entities.CreateEntity();
        padded.Add(entityId, WithPadding(0x00));

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        padded.TrySet(entityId, WithPadding(0xFF));

        var entry = EntryFor<Padded>(report.Complete());
        Assert.AreEqual(1, entry.SurvivingHolders);
        Assert.AreEqual(0, entry.ChangedHolders);
    }

    private struct Padded
    {
        public byte Kind;
        public ushort Amount;
    }

    [TestMethod]
    public void Complete_ComparesEveryElementOfAnInlineArrayField()
    {
        var (components, entities) = CreateWorld();
        components.RegisterPackedPool<WithInlineArray>(static (ref existing, incoming) => existing = incoming);
        var pool = components.GetPackedPool<WithInlineArray>();
        var unchanged = entities.CreateEntity();
        var changed = entities.CreateEntity();
        pool.Add(unchanged, Elements(1, 2, 3));
        pool.Add(changed, Elements(1, 2, 3));

        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        pool.TrySet(changed, Elements(1, 2, 9));

        var entry = EntryFor<WithInlineArray>(report.Complete());
        Assert.AreEqual(1, entry.DistinctValues, "Both holders started with the same elements.");
        Assert.AreEqual(1, entry.ChangedHolders, "A difference in the last element is still a change.");
    }

    private struct WithInlineArray
    {
        public Elements3 Values;
    }

    [InlineArray(3)]
    private struct Elements3
    {
        private ushort _element0;
    }

    private static WithInlineArray Elements(ushort first, ushort second, ushort third)
    {
        var value = default(WithInlineArray);
        value.Values[0] = first;
        value.Values[1] = second;
        value.Values[2] = third;
        return value;
    }

    private static Padded WithPadding(byte padding)
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<Padded>()];
        bytes.Fill(padding);
        var value = MemoryMarshal.Read<Padded>(bytes);
        value.Kind = 1;
        value.Amount = 5;
        return value;
    }

    [TestMethod]
    public void Complete_WithoutBaseline_Throws()
    {
        var (components, entities) = CreateWorld();

        Assert.ThrowsExactly<InvalidOperationException>(() => new PoolMemoryReport(components, entities).Complete());
    }

    [TestMethod]
    public void WriteReport_WritesJsonAndTextWithEveryPool()
    {
        var (components, entities) = CreateWorld();
        components.GetPackedPool<Counter>().Add(entities.CreateEntity(), new Counter { Value = 1 });
        var report = new PoolMemoryReport(components, entities);
        report.CaptureBaseline();
        var directory = Path.Combine(Path.GetTempPath(), $"pool-memory-report-{Guid.NewGuid():N}");

        try
        {
            var path = report.WriteReport(directory, randomSeed: 7, new BenchmarkFrameRange(10, 20));

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(7, json.RootElement.GetProperty("RandomSeed").GetInt32());
            Assert.AreEqual(3, json.RootElement.GetProperty("Pools").GetArrayLength());
            Assert.IsTrue(File.Exists(Path.ChangeExtension(path, ".txt")));
            Assert.IsFalse(report.HasBaseline);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
