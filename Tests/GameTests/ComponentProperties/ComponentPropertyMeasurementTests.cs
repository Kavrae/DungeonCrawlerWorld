using System.Diagnostics;
using System.Reflection;
using System.Text;
using Engine.ECS.Properties;
using Engine.Tags;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;

namespace Tests.GameTests.ComponentProperties;

/// <summary>
/// Reads every property of 10,000 components (2,000 of each of five) through the generated traversal and through cached reflection (Phase 0 also measured the components' ToString overrides, since deleted: PLAN-generated-component-property-access.md). Tagged Performance:
/// `dotnet test Tests/Tests.csproj --filter "TestCategory=Performance&amp;FullyQualifiedName~ComponentPropertyMeasurement" --logger "console;verbosity=detailed"`.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[DoNotParallelize]
public sealed class ComponentPropertyMeasurementTests
{
    private const int PerType = 2_000;
    private const int Repeats = 25;

    private readonly MovementComponent[] _movement = Enumerable.Range(0, PerType).Select(ComponentPropertySamples.Movement).ToArray();
    private readonly InventoryItemStackComponent[] _stacks = Enumerable.Range(0, PerType).Select(ComponentPropertySamples.Stack).ToArray();
    private readonly DeadComponent[] _dead = Enumerable.Range(0, PerType).Select(ComponentPropertySamples.Dead).ToArray();
    private readonly SpawnRecordComponent[] _spawns = Enumerable.Range(0, PerType).Select(ComponentPropertySamples.Spawn).ToArray();
    private readonly PendingWindupComponent[] _windups = Enumerable.Range(0, PerType).Select(ComponentPropertySamples.Windup).ToArray();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GeneratedAgainstReflection_Over10000Components()
    {
        var summing = new SummingVisitor();
        var formatting = new FormattingVisitor();
        var reflection = new ReflectionReader();

        var generated = Measure("Generated traversal, summing visitor (no strings)", () => { VisitAll(summing); return summing.Total; });
        var generatedText = Measure("Generated traversal, formatting visitor (one reused StringBuilder)", () => { formatting.Output.Clear(); VisitAll(formatting); return formatting.Output.Length; });
        var reflected = Measure("Cached reflection (PropertyInfo/FieldInfo.GetValue, recursing into nested structs)", () => reflection.ReadAll(this));

        Assert.IsLessThan(reflected, generated, "The generated traversal should beat cached reflection.");
        Assert.IsLessThan(reflected, generatedText, "Formatting through the traversal should beat cached reflection's reads alone.");
    }

    private void VisitAll(ComponentPropertyVisitor visitor)
    {
        Visit(_movement, visitor);
        Visit(_stacks, visitor);
        Visit(_dead, visitor);
        Visit(_spawns, visitor);
        Visit(_windups, visitor);
    }

    private static void Visit<T>(T[] components, ComponentPropertyVisitor visitor)
        where T : struct
    {
        var access = ComponentPropertyAccessOf<T>.Access!;
        for (var i = 0; i < components.Length; i++)
        {
            access.Read(in components[i], visitor);
        }
    }

    private double Measure(string name, Func<long> pass)
    {
        pass();
        pass();
        var times = new double[Repeats];
        long allocated = 0;
        long sink = 0;
        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            sink += pass();
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            times[repeat] = watch.Elapsed.TotalNanoseconds;
        }

        Array.Sort(times);
        var perComponent = times[Repeats / 2] / (PerType * 5);
        TestContext.WriteLine($"{name}: {perComponent:F1} ns a component (median of {Repeats}), {allocated / (PerType * 5.0):F0} bytes allocated a component [{sink}]");
        return perComponent;
    }

    private sealed class SummingVisitor : ComponentPropertyVisitor
    {
        public long Total;

        public override void Integer(ComponentPropertyDescriptor property, long value) => Total += value;
        public override void Number(ComponentPropertyDescriptor property, double value) => Total += (long)value;
        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Total += value ? 1 : 0;
        public override void Text(ComponentPropertyDescriptor property, string? value) => Total += value?.Length ?? 0;
        public override void Enum(ComponentPropertyDescriptor property, long value) => Total += value;
        public override void Guid(ComponentPropertyDescriptor property, Guid value) => Total += value.GetHashCode();
        public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Total += packedValue;
        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Total += value.IsNone ? 0 : 1;
        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Total += value is null ? 0 : 1;
        public override void Definition(ComponentPropertyDescriptor property, object? value) => Total += value is null ? 0 : 1;
        public override void Absent(ComponentPropertyDescriptor property) => Total++;
        public override void BeginNested(ComponentPropertyDescriptor property) { }
        public override void EndNested(ComponentPropertyDescriptor property) { }
        public override void BeginIndexed(ComponentPropertyDescriptor property, int count) { }
        public override void EndIndexed(ComponentPropertyDescriptor property) { }
    }

    private sealed class FormattingVisitor : ComponentPropertyVisitor
    {
        public StringBuilder Output { get; } = new();

        public override void Integer(ComponentPropertyDescriptor property, long value) => Row(property).Append(value).Append('\n');
        public override void Number(ComponentPropertyDescriptor property, double value) => Row(property).Append(value).Append('\n');
        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Row(property).Append(value).Append('\n');
        public override void Text(ComponentPropertyDescriptor property, string? value) => Row(property).Append(value).Append('\n');
        public override void Enum(ComponentPropertyDescriptor property, long value) => Row(property).Append(value).Append('\n');
        public override void Guid(ComponentPropertyDescriptor property, Guid value) => Row(property).Append(value).Append('\n');
        public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Row(property).Append(packedValue).Append('\n');
        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Row(property).Append(value.IsNone ? "none" : value.Name).Append('\n');
        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Row(property).Append(value is null ? "none" : "query").Append('\n');
        public override void Definition(ComponentPropertyDescriptor property, object? value) => Row(property).Append(value is null ? "none" : "definition").Append('\n');
        public override void Absent(ComponentPropertyDescriptor property) => Row(property).Append("none\n");
        public override void BeginNested(ComponentPropertyDescriptor property) => Row(property).Append('\n');
        public override void EndNested(ComponentPropertyDescriptor property) { }
        public override void BeginIndexed(ComponentPropertyDescriptor property, int count) => Row(property).Append('\n');
        public override void EndIndexed(ComponentPropertyDescriptor property) { }

        private StringBuilder Row(ComponentPropertyDescriptor property) => Output.Append(property.Name).Append(" : ");
    }

    private sealed class ReflectionReader
    {
        private readonly Dictionary<Type, (PropertyInfo[] Properties, FieldInfo[] Fields)> _members = [];

        public long ReadAll(ComponentPropertyMeasurementTests owner)
        {
            long total = 0;
            foreach (var component in owner._movement) total += Read(component);
            foreach (var component in owner._stacks) total += Read(component);
            foreach (var component in owner._dead) total += Read(component);
            foreach (var component in owner._spawns) total += Read(component);
            foreach (var component in owner._windups) total += Read(component);
            return total;
        }

        private long Read(object value)
        {
            var type = value.GetType();
            if (!_members.TryGetValue(type, out var members))
            {
                members = (
                    type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0).ToArray(),
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance));
                _members[type] = members;
            }

            long total = 0;
            foreach (var property in members.Properties)
            {
                total += Leaf(property.GetValue(value));
            }

            foreach (var field in members.Fields)
            {
                total += Leaf(field.GetValue(value));
            }

            return total;
        }

        private long Leaf(object? value) => value switch
        {
            null => 0,
            { } nested when nested.GetType() is { IsValueType: true, IsPrimitive: false, IsEnum: false } type
                            && type != typeof(Guid) && !type.IsGenericType && type.Namespace?.StartsWith("Game", StringComparison.Ordinal) == true
                            || nested is Engine.Math.Vector3Int or Engine.Math.Vector2Byte => Read(nested),
            _ => value.GetHashCode(),
        };
    }
}
