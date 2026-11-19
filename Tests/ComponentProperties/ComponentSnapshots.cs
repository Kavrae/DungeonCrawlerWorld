using System.Globalization;
using System.Text;
using Engine.ECS.Components;
using Engine.ECS.Properties;
using Engine.Tags;
using Game.Modules.Actions;

namespace Tests.ComponentProperties;

/// <summary>Every component instance an entity holds, as text read through its property table: for tests that compare one entity's state with another's.</summary>
internal static class ComponentSnapshots
{
    /// <summary>One entry per instance, in pool registration order: its type and "Name=value;" for every property, nested ones in braces. A definition is its name.</summary>
    public static List<(Type ComponentType, string Value)> Of(ComponentManager componentManager, int entityId)
    {
        var snapshots = new List<(Type, string)>();
        foreach (var pool in componentManager.PoolsInRegistrationOrder)
        {
            if (pool is not IDescribedComponentPool described || !pool.Has(entityId))
            {
                continue;
            }

            var visitor = new SnapshotVisitor();
            described.ReadProperties(entityId, visitor);
            snapshots.AddRange(visitor.Instances.Select(text => (pool.ComponentType, text)));
        }

        return snapshots;
    }

    private sealed class SnapshotVisitor : ComponentPropertyVisitor
    {
        private StringBuilder _current = new();

        public List<string> Instances { get; } = [];

        public override void BeginInstance(ComponentPropertyTable table, int instance, uint version) => _current = new StringBuilder();

        public override void EndInstance(ComponentPropertyTable table, int instance) => Instances.Add(_current.ToString());

        public override void Integer(ComponentPropertyDescriptor property, long value) => Add(property, value.ToString(CultureInfo.InvariantCulture));

        public override void Number(ComponentPropertyDescriptor property, double value) => Add(property, value.ToString("R", CultureInfo.InvariantCulture));

        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Add(property, value.ToString());

        public override void Text(ComponentPropertyDescriptor property, string? value) => Add(property, value ?? "null");

        public override void Enum(ComponentPropertyDescriptor property, long value) => Add(property, value.ToString(CultureInfo.InvariantCulture));

        public override void Guid(ComponentPropertyDescriptor property, Guid value) => Add(property, value.ToString());

        public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Add(property, packedValue.ToString(CultureInfo.InvariantCulture));

        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Add(property, value.IsNone ? "none" : value.Name);

        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Add(property, value?.ToString() ?? "none");

        public override void Definition(ComponentPropertyDescriptor property, object? value) =>
            Add(property, value is ActivatableDefinition definition ? definition.Name : value?.GetType().Name ?? "none");

        public override void Absent(ComponentPropertyDescriptor property) => Add(property, "absent");

        public override void BeginNested(ComponentPropertyDescriptor property) => _current.Append(property.Name).Append('{');

        public override void EndNested(ComponentPropertyDescriptor property) => _current.Append("};");

        public override void BeginIndexed(ComponentPropertyDescriptor property, int count) => _current.Append(property.Name).Append('[');

        public override void EndIndexed(ComponentPropertyDescriptor property) => _current.Append("];");

        private void Add(ComponentPropertyDescriptor property, string value) => _current.Append(property.Name).Append('=').Append(value).Append(';');
    }
}
