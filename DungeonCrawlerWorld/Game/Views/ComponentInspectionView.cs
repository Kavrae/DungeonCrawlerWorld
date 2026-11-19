using System.Globalization;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Engine.Tags;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Lootboxes;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Spawning;
using Game.World;

namespace Game.Views;

/// <summary>One component instance an entity holds: which, which of its kind, its version, and whether its text changes with time alone.</summary>
/// <param name="ShowsTime">It holds a frame (shown in seconds from now) or a row derived from another component (health against its modified maximum), so its text can change while its version doesn't.</param>
public readonly record struct InspectedComponentInstance(Type ComponentType, string Name, int Instance, int InstanceCount, uint Version, bool ShowsTime);

/// <summary>One row of a component's inspection: its depth under nested and indexed properties, its label and its value as shown.</summary>
/// <param name="Property">What the row is, for an editor: its kind, meaning and flags; null for a derived row.</param>
/// <param name="Path">Where the row's value is, for an edit: a property index, then one per nested or indexed level; empty for a derived row.</param>
/// <param name="RawValue">The value as an edit would write it back.</param>
/// <param name="IsHeading">A nested or indexed property's own row, with its values in the rows under it.</param>
public readonly record struct InspectedPropertyRow(int Depth, string Label, string Value, bool IsReadOnly, ComponentPropertyDescriptor? Property = null, int[]? Path = null, ComponentPropertyValue RawValue = default, bool IsHeading = false);

/// <summary>A component instance with its rows, for a world a caller doesn't hold (an unsimulated creature's rebuilt defaults).</summary>
public sealed record InspectedComponentBlock(InspectedComponentInstance Instance, IReadOnlyList<InspectedPropertyRow> Rows);

/// <summary>An entity's components as named rows, read through their property tables: each value as what it stands for -- a frame as seconds from now, an id as its name, a key as its entity.</summary>
/// <remarks>
/// Admin inspection only, never gameplay. Instances come sorted by component name, then by their position among the
/// entity's instances of that type. Health adds its modified maximum as a derived row (SimpleHealthComponent, and per part
/// for BodyPartStateComponent); body part rows are named by the race and only its parts are shown.
/// </remarks>
public sealed class ComponentInspectionView
{
    private readonly ComponentManager _componentManager;
    private readonly BlueprintRegistry _definitions;
    private readonly ComponentPropertyMeanings _meanings;
    private readonly Dictionary<ComponentPropertyTable, bool> _showsTime = [];
    private readonly List<uint> _instanceVersions = [];

    public ComponentInspectionView(ComponentManager componentManager, BlueprintRegistry definitions, ComponentPropertyMeanings meanings)
    {
        _componentManager = componentManager;
        _definitions = definitions;
        _meanings = meanings;
    }

    /// <summary>Every described component instance the entity holds, sorted, with versions; reads no value.</summary>
    public void CopyInstances(int entityId, List<InspectedComponentInstance> destination) => CopyInstances(_componentManager, entityId, destination);

    /// <summary>The rows of the entity's <paramref name="instance"/>-th <paramref name="componentType"/>; none when it holds no such instance.</summary>
    public void CopyRows(int entityId, Type componentType, int instance, List<InspectedPropertyRow> destination) =>
        CopyRows(_componentManager, entityId, componentType, instance, destination);

    /// <summary>The entity's key: what an edit names it by, so an edit never reaches whatever later holds its id.</summary>
    public EntityKey KeyOf(int entityId) => _componentManager.Keys.GetKey(entityId);

    /// <summary>The values a picker offers for a property's meaning, for this entity; false when it is entered by typing.</summary>
    public bool TryGetChoices(int entityId, ComponentPropertyMeaning meaning, out IReadOnlyList<ComponentPropertyChoice> choices)
    {
        if (_meanings.TryGet(meaning, out var resolver) && resolver.TryGetChoices(new ComponentPropertyContext(_componentManager, entityId), out choices))
        {
            return true;
        }

        choices = [];
        return false;
    }

    /// <summary>What an unsimulated creature's spawn record rebuilds to, in a staging world, as blocks of rows; the creature itself is never built.</summary>
    public void CopyUnsimulatedDefaults(int entityId, SpawnRecordRebuilder rebuilder, List<InspectedComponentBlock> destination)
    {
        destination.Clear();
        var record = _componentManager.GetDirectPool<SpawnRecordComponent>().GetReadonly(entityId);
        rebuilder.Rebuild(record, (stagingComponents, stagingEntityId) =>
        {
            var instances = new List<InspectedComponentInstance>();
            CopyInstances(stagingComponents, stagingEntityId, instances);
            foreach (var instance in instances)
            {
                var rows = new List<InspectedPropertyRow>();
                CopyRows(stagingComponents, stagingEntityId, instance.ComponentType, instance.Instance, rows);
                destination.Add(new InspectedComponentBlock(instance, rows));
            }
        });
    }

    private void CopyInstances(ComponentManager source, int entityId, List<InspectedComponentInstance> destination)
    {
        destination.Clear();
        foreach (var pool in source.PoolsInRegistrationOrder)
        {
            if (pool is not IDescribedComponentPool { PropertyTable: { } table } described || !pool.Has(entityId))
            {
                continue;
            }

            _instanceVersions.Clear();
            var count = described.CopyInstanceVersions(entityId, _instanceVersions);
            var name = pool.ComponentType.Name;
            var showsTime = ShowsTime(table) || HasDerivedRows(pool.ComponentType);
            for (var instance = 0; instance < count; instance++)
            {
                destination.Add(new InspectedComponentInstance(pool.ComponentType, name, instance, count, _instanceVersions[instance], showsTime));
            }
        }

        destination.Sort(static (left, right) =>
        {
            var byName = string.CompareOrdinal(left.Name, right.Name);
            return byName != 0 ? byName : left.Instance.CompareTo(right.Instance);
        });
    }

    private void CopyRows(ComponentManager source, int entityId, Type componentType, int instance, List<InspectedPropertyRow> destination)
    {
        destination.Clear();
        if (!source.TryGetPool(componentType, out var candidate) || candidate is not IDescribedComponentPool pool)
        {
            return;
        }

        var visitor = new RowVisitor(new ComponentPropertyContext(source, entityId), _meanings, destination);
        pool.ReadInstanceProperties(entityId, instance, visitor);
        AddDerivedRows(source, entityId, componentType, destination);
    }

    private bool ShowsTime(ComponentPropertyTable table)
    {
        if (!_showsTime.TryGetValue(table, out var showsTime))
        {
            showsTime = HoldsAFrame(table);
            _showsTime[table] = showsTime;
        }

        return showsTime;
    }

    private static bool HoldsAFrame(ComponentPropertyTable table) =>
        table.Properties.Any(static property =>
            property.Meaning.Name == ComponentPropertyMeaningNames.Frame
            || property.Element?.Meaning.Name == ComponentPropertyMeaningNames.Frame
            || (property.Nested is { } nested && HoldsAFrame(nested)));

    private static bool HasDerivedRows(Type componentType) =>
        componentType == typeof(SimpleHealthComponent) || componentType == typeof(BodyPartStateComponent);

    /// <summary>Health against its modified maximum: the stored maximum is a base, so what the entity can actually reach is a row of its own.</summary>
    private void AddDerivedRows(ComponentManager source, int entityId, Type componentType, List<InspectedPropertyRow> destination)
    {
        var statModifiers = source.GetMultiPool<StatModifierComponent>();
        if (componentType == typeof(SimpleHealthComponent) && source.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health))
        {
            var maximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, health.BaseMaximumHealth);
            destination.Add(new InspectedPropertyRow(0, "MaximumHealth (modified)", FormatNumber(maximum), IsReadOnly: true));
        }
        else if (componentType == typeof(BodyPartStateComponent))
        {
            destination.Add(new InspectedPropertyRow(0, "MaximumHealth (modified)", "", IsReadOnly: true));
            foreach (var part in EntityBodyParts.For(source, _definitions).Parts(entityId))
            {
                var maximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.BaseMaximumHealth);
                destination.Add(new InspectedPropertyRow(1, part.Name, FormatNumber(maximum), IsReadOnly: true));
            }
        }
    }

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Formats one instance's values as rows, each through its meaning's resolver where it has one; an ActionSource is one row naming the source.</summary>
    /// <remarks>Reads one instance (IDescribedComponentPool.ReadInstanceProperties), so every value it is given is formatted.</remarks>
    private sealed class RowVisitor(ComponentPropertyContext context, ComponentPropertyMeanings meanings, List<InspectedPropertyRow> rows) : ComponentPropertyVisitor
    {
        private readonly Stack<Level> _levels = new();
        private SourceParts _source;

        public override void Integer(ComponentPropertyDescriptor property, long value) =>
            Add(property, (meanings.TryGet(property.Meaning, out var resolver) ? resolver.DescribeInteger(context, value) : null) ?? ComponentPropertyIntegers.Format(property.ValueType, value), ComponentPropertyValue.FromInteger(value), value);

        public override void Number(ComponentPropertyDescriptor property, double value) => Add(property, FormatNumber(value), ComponentPropertyValue.FromNumber(value));

        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Add(property, value ? "true" : "false", ComponentPropertyValue.FromBoolean(value));

        public override void Text(ComponentPropertyDescriptor property, string? value) =>
            Add(property, value is null ? "none" : $"\"{value}\"", ComponentPropertyValue.FromText(value), text: value);

        public override void Enum(ComponentPropertyDescriptor property, long value) =>
            Add(property, System.Enum.ToObject(property.ValueType, value).ToString() ?? value.ToString(CultureInfo.InvariantCulture), ComponentPropertyValue.FromEnum(value), value);

        public override void Guid(ComponentPropertyDescriptor property, Guid value) =>
            Add(property, meanings.TryGet(property.Meaning, out var resolver) && resolver.DescribeGuid(context, value) is { } name ? $"{name} ({value})" : value.ToString(), ComponentPropertyValue.FromGuid(value));

        public override void Color(ComponentPropertyDescriptor property, uint packedValue) =>
            Add(property, $"#{packedValue & 0xFF:X2}{(packedValue >> 8) & 0xFF:X2}{(packedValue >> 16) & 0xFF:X2}{packedValue >> 24:X2}", ComponentPropertyValue.FromColor(packedValue));

        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) =>
            Add(property, value.IsNone ? "none" : value.Name, ComponentPropertyValue.FromTag(value.IsNone ? "" : value.Name));

        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) =>
            Add(property, value?.ToString() ?? "none", ComponentPropertyValue.FromText(value?.ToString()));

        public override void Definition(ComponentPropertyDescriptor property, object? value) => Add(property, value switch
        {
            null => "none",
            ActivatableDefinition definition => $"{definition.Name} ({definition.Id})",
            LootboxReward reward => $"{reward.Rarity} loot box ({reward.TypeId})",
            _ => value.GetType().Name,
        }, ComponentPropertyValue.FromText(value?.ToString()));

        public override void Absent(ComponentPropertyDescriptor property)
        {
            if (_levels.TryPeek(out var level) && level.IsIndexed)
            {
                level.NextRow++;
                return;
            }

            Add(property, "none", ComponentPropertyValue.Absent);
        }

        public override void BeginNested(ComponentPropertyDescriptor property)
        {
            if (!IsCollectingSource && property.ValueType == typeof(ActionSource))
            {
                _source = default;
                _levels.Push(new Level(property, NextStep(property), isIndexed: false, isSource: true));
                return;
            }

            Header(property);
            _levels.Push(new Level(property, NextStep(property), isIndexed: false, isSource: false));
        }

        public override void EndNested(ComponentPropertyDescriptor property)
        {
            var level = _levels.Pop();
            if (level.IsSource)
            {
                AddRow(property, _source.Describe(), default, isReadOnly: true);
            }
        }

        public override void BeginIndexed(ComponentPropertyDescriptor property, int count)
        {
            Header(property);
            _levels.Push(new Level(property, NextStep(property), isIndexed: true, isSource: false));
        }

        public override void EndIndexed(ComponentPropertyDescriptor property) => _levels.Pop();

        private bool IsCollectingSource => _levels.TryPeek(out var level) && level.IsSource;

        /// <summary>The path step for a level opening here: an element of an indexed level takes its next row, anything else is its property's index.</summary>
        private int NextStep(ComponentPropertyDescriptor property) =>
            _levels.TryPeek(out var parent) && parent.IsIndexed ? parent.NextRow++ : property.Index;

        private void Header(ComponentPropertyDescriptor property) => AddRow(property, "", default, isHeading: true);

        private void Add(ComponentPropertyDescriptor property, string formatted, ComponentPropertyValue raw, long integer = 0, string? text = null)
        {
            if (IsCollectingSource)
            {
                _source.Take(property.Name, integer, text, formatted);
                return;
            }

            if (_levels.TryPeek(out var level) && level.IsIndexed)
            {
                var index = level.NextRow++;
                var label = index.ToString(CultureInfo.InvariantCulture);
                if (meanings.TryGet(level.Property.Meaning, out var rowResolver) && !rowResolver.TryNameRow(context, index, out label))
                {
                    return;
                }

                rows.Add(new InspectedPropertyRow(_levels.Count, label, formatted, property.IsReadOnly || IsUnderReadOnly, property, PathTo(index), raw));
                return;
            }

            AddRow(property, formatted, raw);
        }

        private void AddRow(ComponentPropertyDescriptor property, string value, ComponentPropertyValue raw, bool isHeading = false, bool isReadOnly = false) =>
            rows.Add(new InspectedPropertyRow(_levels.Count, property.Name, value, isReadOnly || property.IsReadOnly || IsUnderReadOnly, property, PathTo(property.Index), raw, isHeading));

        private bool IsUnderReadOnly => _levels.Any(static level => level.Property.IsReadOnly);

        /// <summary>The path to a value at this level: each enclosing level's step (its property index, or its row in an indexed level), then the value's own index (an indexed level's row).</summary>
        private int[] PathTo(int index)
        {
            var path = new int[_levels.Count + 1];
            var position = _levels.Count;
            foreach (var enclosing in _levels)
            {
                path[--position] = enclosing.Step;
            }

            path[^1] = index;
            return path;
        }


        /// <summary>One nested or indexed level being read: what it is, and for an indexed one the next row's index.</summary>
        private sealed class Level(ComponentPropertyDescriptor property, int step, bool isIndexed, bool isSource)
        {
            public ComponentPropertyDescriptor Property { get; } = property;

            public int Step { get; } = step;

            public bool IsIndexed { get; } = isIndexed;

            public bool IsSource { get; } = isSource;

            public int NextRow { get; set; }
        }

        /// <summary>An ActionSource's rows gathered into one description: an entity by name, terrain and auras by what they are.</summary>
        private struct SourceParts
        {
            private string? _kind;
            private string? _entity;
            private string? _terrain;
            private string? _aura;
            private string? _name;
            private long _crawlerNumber;

            public void Take(string row, long integer, string? text, string formatted)
            {
                switch (row)
                {
                    case nameof(ActionSource.Kind): _kind = formatted; break;
                    case nameof(ActionSource.Key): _entity = formatted; break;
                    case nameof(ActionSource.TerrainTypeId): _terrain = formatted; break;
                    case nameof(ActionSource.AuraId): _aura = formatted; break;
                    case "Name": _name = text; break;
                    case "CrawlerNumber": _crawlerNumber = integer; break;
                }
            }

            public readonly string Describe() => _kind switch
            {
                nameof(ActionSourceKind.Entity) => $"{_name ?? "unnamed"}{(_crawlerNumber > 0 ? $" #{_crawlerNumber}" : "")} -- {_entity}",
                nameof(ActionSourceKind.Terrain) => $"terrain: {_terrain}",
                nameof(ActionSourceKind.Aura) => $"aura: {_aura}",
                _ => _kind ?? "none",
            };
        }
    }
}
