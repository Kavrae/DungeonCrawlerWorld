using System.Buffers;
using System.Text;
using System.Text.Json;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Relationships;
using Engine.Json;
using Engine.Tags;

namespace Engine.ECS.Properties;

/// <summary>What a capture knows while it writes: the frame it was taken on, the entities it holds, and the ordinals it has given remapped values.</summary>
public sealed class EntitySnapshotCapture
{
    private readonly Dictionary<EntityKey, int> _entityIndexByKey = [];
    private readonly Dictionary<(string Meaning, long Value), int> _ordinals = [];
    private readonly Dictionary<string, int> _nextOrdinal = new(StringComparer.Ordinal);

    internal EntitySnapshotCapture(long frame, IEnumerable<EntityKey> keys)
    {
        Frame = frame;
        var index = 0;
        foreach (var key in keys)
        {
            _entityIndexByKey[key] = index++;
        }
    }

    /// <summary>The simulation frame the snapshot was taken on; frames are saved relative to it.</summary>
    public long Frame { get; }

    /// <summary>The position in the snapshot of the entity a key names, if the snapshot holds it.</summary>
    public bool TryGetEntityIndex(EntityKey key, out int index) => _entityIndexByKey.TryGetValue(key, out index);

    /// <summary>The snapshot's ordinal for a remapped value, given in the order values are first met, from 1.</summary>
    public int OrdinalOf(string meaning, long value)
    {
        if (_ordinals.TryGetValue((meaning, value), out var ordinal))
        {
            return ordinal;
        }

        ordinal = _nextOrdinal.GetValueOrDefault(meaning) + 1;
        _nextOrdinal[meaning] = ordinal;
        _ordinals[(meaning, value)] = ordinal;
        return ordinal;
    }
}

/// <summary>What a restore knows while it reads: the frame it restores on, the keys of the entities it created, and the values it has issued for the snapshot's ordinals.</summary>
public sealed class EntitySnapshotRestore
{
    private readonly List<EntityKey> _keys = [];
    private readonly Dictionary<(string Meaning, int Ordinal), long> _issued = [];

    internal EntitySnapshotRestore(long frame) => Frame = frame;

    /// <summary>The simulation frame the snapshot is restored on; saved frames are restored relative to it.</summary>
    public long Frame { get; }

    /// <summary>The key of the entity restored for the snapshot's entity at index.</summary>
    public bool TryGetEntityKey(int index, out EntityKey key)
    {
        key = (uint)index < (uint)_keys.Count ? _keys[index] : default;
        return !key.IsNone;
    }

    /// <summary>The value issued for a snapshot ordinal, issued on first ask so every reference to it gets the same one.</summary>
    public long Issued(string meaning, int ordinal, Func<long> issue)
    {
        if (!_issued.TryGetValue((meaning, ordinal), out var value))
        {
            value = issue();
            _issued[(meaning, ordinal)] = value;
        }

        return value;
    }

    internal void AddEntity(EntityKey key) => _keys.Add(key);
}

/// <summary>One entity as a snapshot saved it, handed to the host so it can create and place the restored entity from its saved components.</summary>
public sealed class EntitySnapshotEntity
{
    private readonly JsonElement _components;
    private readonly ComponentPropertyPersistence _persistence;
    private readonly EntitySnapshotRestore _restore;

    internal EntitySnapshotEntity(int index, JsonElement components, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore)
    {
        Index = index;
        _components = components;
        _persistence = persistence;
        _restore = restore;
    }

    /// <summary>The entity's position in the snapshot.</summary>
    public int Index { get; }

    /// <summary>Whether the entity saved an instance of T.</summary>
    public bool Holds<T>() => _components.TryGetProperty(EntitySnapshots.TypeNameOf(typeof(T)), out _);

    /// <summary>The entity's one saved instance of T, built from its saved values without adding it anywhere.</summary>
    public bool TryBuild<T>(out T component, out string? problem)
        where T : struct
    {
        component = default;
        if (ComponentPropertyAccessOf<T>.Access is not { } access)
        {
            problem = $"{typeof(T).Name} has no property table";
            return false;
        }

        if (!_components.TryGetProperty(EntitySnapshots.TypeNameOf(typeof(T)), out var instances) || instances.GetArrayLength() != 1)
        {
            problem = $"the entity saved no single {typeof(T).Name}";
            return false;
        }

        var leaves = new List<ComponentPropertyLeaf>();
        problem = EntitySnapshots.Decode(instances[0], access.Table, _persistence, _restore, leaves)
            ?? ComponentPropertyRestoring.TryBuildOne<T>([leaves], out component);
        return problem is null;
    }

    internal JsonElement Components => _components;
}

/// <summary>What the game does around a restore: create each entity, restore the components it owns itself (where it stands, its tier), and finish each entity once its components are back.</summary>
public interface IEntitySnapshotHost
{
    /// <summary>Creates the entity a saved one is restored onto, before any component of any entity is restored; returns its id.</summary>
    int CreateEntity(EntitySnapshotEntity saved);

    /// <summary>Whether the host restores this component type itself (in CreateEntity or Restored) instead of its pool.</summary>
    bool RestoresItself(Type componentType);

    /// <summary>Finishes an entity once every component of every entity is restored: places it, recomputes what it caches.</summary>
    void Restored(int entityId, EntitySnapshotEntity saved);
}

/// <summary>A capture's result: the snapshot as JSON, or why it couldn't be taken.</summary>
public sealed record EntitySnapshotCaptureResult(string? Json, IReadOnlyList<string> Problems);

/// <summary>A restore's result: the entity created for each saved one, in the snapshot's order, and every component left off with why.</summary>
public sealed record EntitySnapshotRestoreResult(IReadOnlyList<int> EntityIds, IReadOnlyList<string> Problems);

/// <summary>Saves a set of entities' described components as JSON through their tables, and restores them onto fresh entities through each pool's Add, so timers schedule and relationships check.</summary>
/// <remarks>
/// Decisions 16-19 of PLAN-generated-component-property-access.md: every saved member is restorable, session-scoped ids
/// are remapped within the snapshot, frames are relative to the capture, and a snapshot is a set of entities. A value is
/// saved by its meaning's persistent form (ComponentPropertyPersistence), a definition through the game's codec, and
/// anything else as itself (EntitySnapshotValues); a derived or [NotSaved] member is never saved, and a relationship's
/// sources are rebuilt from the links. Links are restored after every other component, so their targets exist. Never
/// gameplay: a test consumer today, the per-pool half of a save.
/// </remarks>
public static class EntitySnapshots
{
    public const int Format = 1;

    private const string FormatKey = "format";
    private const string FrameKey = "frame";
    private const string EntitiesKey = "entities";

    /// <summary>A component type's name in a snapshot: its full name, the contract a save keys on.</summary>
    public static string TypeNameOf(Type componentType) => componentType.FullName ?? componentType.Name;

    /// <summary>Every saved property of every described pool whose meaning or definition type has no persistent form, and every pool with no table, by name; empty when any entity can be saved.</summary>
    public static IReadOnlyList<string> Check(ComponentManager components, ComponentPropertyPersistence persistence)
    {
        var problems = new List<string>();
        foreach (var pool in components.PoolsInRegistrationOrder)
        {
            if (IsRebuiltByItsRelationship(pool.ComponentType))
            {
                continue;
            }

            if (pool is not IDescribedComponentPool { PropertyTable: { } table })
            {
                problems.Add($"{pool.ComponentType.Name} has no property table.");
                continue;
            }

            CheckProperties(table.Properties, pool.ComponentType.Name, persistence, problems);
        }

        return problems;
    }

    /// <summary>The entities' components, every one saved; refused, with every problem, when a value has no persistent form.</summary>
    public static EntitySnapshotCaptureResult Capture(ComponentManager components, IReadOnlyList<int> entityIds, long frame, ComponentPropertyPersistence persistence)
    {
        var capture = new EntitySnapshotCapture(frame, entityIds.Select(components.Keys.GetKey));
        var problems = new List<string>();
        var undescribed = new HashSet<Type>();
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, JsonWriting.Compact))
        {
            var visitor = new CaptureVisitor(json, persistence, capture, problems);
            json.WriteStartObject();
            json.WriteNumber(FormatKey, Format);
            json.WriteNumber(FrameKey, frame);
            json.WriteStartArray(EntitiesKey);
            foreach (var entityId in entityIds)
            {
                json.WriteStartObject();
                foreach (var pool in components.PoolsInRegistrationOrder)
                {
                    if (IsRebuiltByItsRelationship(pool.ComponentType) || !pool.Has(entityId))
                    {
                        continue;
                    }

                    if (pool is not IDescribedComponentPool { PropertyTable: not null } described)
                    {
                        if (undescribed.Add(pool.ComponentType))
                        {
                            problems.Add($"{pool.ComponentType.Name} has no property table.");
                        }

                        continue;
                    }

                    visitor.ComponentName = pool.ComponentType.Name;
                    json.WriteStartArray(TypeNameOf(pool.ComponentType));
                    described.ReadProperties(entityId, visitor);
                    json.WriteEndArray();
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return problems.Count > 0
            ? new EntitySnapshotCaptureResult(null, problems)
            : new EntitySnapshotCaptureResult(Encoding.UTF8.GetString(buffer.WrittenSpan), problems);
    }

    /// <summary>Restores every saved entity onto a fresh one the host creates, every link last; a component that can't be restored is left off and listed.</summary>
    /// <remarks>Between frames. Every entity is created before any component is restored, so a key naming another saved entity resolves to its restored one.</remarks>
    public static EntitySnapshotRestoreResult Restore(ComponentManager components, string json, long frame, ComponentPropertyPersistence persistence, IEntitySnapshotHost host)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new EntitySnapshotRestoreResult([], [$"Not a snapshot: {exception.Message}"]);
        }

        using var _ = document;
        var root = document.RootElement;
        if (ShapeProblemOf(root) is { } shapeProblem)
        {
            return new EntitySnapshotRestoreResult([], [shapeProblem]);
        }

        var restore = new EntitySnapshotRestore(frame);
        var saved = new List<EntitySnapshotEntity>();
        var entityIds = new List<int>();
        foreach (var entity in root.GetProperty(EntitiesKey).EnumerateArray())
        {
            var savedEntity = new EntitySnapshotEntity(saved.Count, entity, persistence, restore);
            var entityId = host.CreateEntity(savedEntity);
            saved.Add(savedEntity);
            entityIds.Add(entityId);
            restore.AddEntity(components.Keys.GetKey(entityId));
        }

        var poolsByName = new Dictionary<string, IDescribedComponentPool>(StringComparer.Ordinal);
        foreach (var pool in components.PoolsInRegistrationOrder)
        {
            if (pool is IDescribedComponentPool described)
            {
                poolsByName[TypeNameOf(pool.ComponentType)] = described;
            }
        }

        var problems = new List<string>();
        foreach (var links in (bool[])[false, true])
        {
            for (var index = 0; index < saved.Count; index++)
            {
                foreach (var component in saved[index].Components.EnumerateObject())
                {
                    if (!poolsByName.TryGetValue(component.Name, out var pool))
                    {
                        problems.Add($"Entity {index}: {component.Name} isn't registered in this build; left off.");
                        continue;
                    }

                    if (typeof(IRelationshipLink).IsAssignableFrom(pool.ComponentType) != links || host.RestoresItself(pool.ComponentType))
                    {
                        continue;
                    }

                    if (RestoreComponent(pool, entityIds[index], component.Value, persistence, restore) is { } problem)
                    {
                        problems.Add($"Entity {index}: {pool.ComponentType.Name} left off: {problem}");
                    }
                }
            }
        }

        for (var index = 0; index < saved.Count; index++)
        {
            host.Restored(entityIds[index], saved[index]);
        }

        return new EntitySnapshotRestoreResult(entityIds, problems);
    }

    /// <summary>What is wrong with the snapshot's outline -- its format, its entities, each entity's components as lists of instances -- or null; checked before any entity is created, so a malformed snapshot creates nothing.</summary>
    /// <remarks>An instance's own values are checked as each is decoded, and a bad one leaves only its component off.</remarks>
    private static string? ShapeProblemOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(FormatKey, out var format)
            || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out var formatNumber)
            || formatNumber != Format)
        {
            return $"Not a format {Format} snapshot.";
        }

        if (!root.TryGetProperty(EntitiesKey, out var entities) || entities.ValueKind != JsonValueKind.Array)
        {
            return $"The snapshot's {EntitiesKey} isn't a list.";
        }

        var index = 0;
        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
            {
                return $"Entity {index} is saved as {entity.ValueKind}, not an object.";
            }

            foreach (var component in entity.EnumerateObject())
            {
                if (component.Value.ValueKind != JsonValueKind.Array)
                {
                    return $"Entity {index}: {component.Name} is saved as {component.Value.ValueKind}, not a list of instances.";
                }
            }

            index++;
        }

        return null;
    }

    private static string? RestoreComponent(IDescribedComponentPool pool, int entityId, JsonElement instances, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore)
    {
        if (pool.PropertyTable is not { } table)
        {
            return "it has no property table";
        }

        var decoded = new List<IReadOnlyList<ComponentPropertyLeaf>>();
        foreach (var instance in instances.EnumerateArray())
        {
            var leaves = new List<ComponentPropertyLeaf>();
            if (Decode(instance, table, persistence, restore, leaves) is { } problem)
            {
                return problem;
            }

            decoded.Add(leaves);
        }

        return pool.Restore(entityId, decoded);
    }

    /// <summary>An instance's saved values, walked in the table's order, as the leaves a restore writes; null, or what is wrong with it.</summary>
    internal static string? Decode(JsonElement instance, ComponentPropertyTable table, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore, List<ComponentPropertyLeaf> leaves) =>
        DecodeObject(instance, table.Properties, table.DescribedType.Name, persistence, restore, [], leaves);

    private static string? DecodeObject(JsonElement element, IReadOnlyList<ComponentPropertyDescriptor> properties, string owner, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore, List<int> path, List<ComponentPropertyLeaf> leaves)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return $"{owner} is saved as {element.ValueKind}, not an object";
        }

        foreach (var saved in element.EnumerateObject())
        {
            if (!IsSavedPropertyName(properties, saved.Name))
            {
                return $"{owner}.{saved.Name} isn't a saved property";
            }
        }

        foreach (var property in properties)
        {
            if (!IsSaved(property))
            {
                continue;
            }

            if (!element.TryGetProperty(property.Name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if ((property.Flags & ComponentPropertyFlags.Optional) != 0)
                {
                    leaves.Add(new ComponentPropertyLeaf([.. path, property.Index], ComponentPropertyValue.Absent));
                }

                continue;
            }

            path.Add(property.Index);
            var problem = DecodeProperty(value, property, $"{owner}.{property.Name}", persistence, restore, path, leaves);
            path.RemoveAt(path.Count - 1);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? DecodeProperty(JsonElement value, ComponentPropertyDescriptor property, string name, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore, List<int> path, List<ComponentPropertyLeaf> leaves)
    {
        switch (property.Kind)
        {
            case ComponentPropertyKind.Nested when property.Nested is { } nested:
                return DecodeObject(value, nested.Properties, name, persistence, restore, path, leaves);
            case ComponentPropertyKind.Indexed when property.Element is { } element:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    return $"{name} is saved as {value.ValueKind}, not a list";
                }

                var row = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Null)
                    {
                        path.Add(row);
                        var problem = DecodeProperty(item, element, $"{name}[{row}]", persistence, restore, path, leaves);
                        path.RemoveAt(path.Count - 1);
                        if (problem is not null)
                        {
                            return problem;
                        }
                    }

                    row++;
                }

                return null;
        }

        if (!TryDecodeLeaf(value, property, persistence, restore, out var leaf, out var leafProblem))
        {
            return $"{name}: {leafProblem}";
        }

        leaves.Add(new ComponentPropertyLeaf([.. path], leaf));
        return null;
    }

    private static bool TryDecodeLeaf(JsonElement element, ComponentPropertyDescriptor property, ComponentPropertyPersistence persistence, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
    {
        if (property.Meaning.Name is { } meaning)
        {
            if (!persistence.TryGetMeaning(meaning, out var meaningPersistence))
            {
                value = default;
                problem = $"meaning {meaning} has no persistent form";
                return false;
            }

            return meaningPersistence.TryRead(element, property, restore, out value, out problem);
        }

        if (property.Kind == ComponentPropertyKind.Definition)
        {
            if (!persistence.TryGetDefinition(property.ValueType, out var definitionPersistence))
            {
                value = default;
                problem = $"a {property.ValueType.Name} has no persistent form";
                return false;
            }

            if (!definitionPersistence.TryRead(element, out var definition, out problem))
            {
                value = default;
                return false;
            }

            value = ComponentPropertyValue.FromDefinition(definition);
            return true;
        }

        return EntitySnapshotValues.TryRead(element, property, out value, out problem);
    }

    private static bool IsSaved(ComponentPropertyDescriptor property) =>
        (property.Flags & (ComponentPropertyFlags.NotSaved | ComponentPropertyFlags.Derived)) == 0;

    private static bool IsSavedPropertyName(IReadOnlyList<ComponentPropertyDescriptor> properties, string name)
    {
        foreach (var property in properties)
        {
            if (property.Name == name)
            {
                return IsSaved(property);
            }
        }

        return false;
    }

    private static bool IsRebuiltByItsRelationship(Type componentType) =>
        componentType.IsGenericType && componentType.GetGenericTypeDefinition() == typeof(RelatedSourceComponent<>);

    private static void CheckProperties(IReadOnlyList<ComponentPropertyDescriptor> properties, string owner, ComponentPropertyPersistence persistence, List<string> problems)
    {
        foreach (var property in properties)
        {
            if (!IsSaved(property))
            {
                continue;
            }

            var name = $"{owner}.{property.Name}";
            if (property.Meaning.Name is { } meaning && !persistence.TryGetMeaning(meaning, out _))
            {
                problems.Add($"{name}: meaning {meaning} has no persistent form.");
            }

            if (property.Kind == ComponentPropertyKind.Definition && !persistence.TryGetDefinition(property.ValueType, out _))
            {
                problems.Add($"{name}: a {property.ValueType.Name} has no persistent form.");
            }

            if (property.Nested is { } nested)
            {
                CheckProperties(nested.Properties, name, persistence, problems);
            }

            if (property.Element is { } element)
            {
                CheckProperties([element], name, persistence, problems);
            }
        }
    }

    /// <summary>Writes each instance as an object of its saved properties: nested values as objects, indexed rows as lists (null for an absent row), an absent optional left out.</summary>
    private sealed class CaptureVisitor(Utf8JsonWriter json, ComponentPropertyPersistence persistence, EntitySnapshotCapture capture, List<string> problems) : ComponentPropertyVisitor
    {
        private readonly Stack<bool> _inList = new();
        private int _skipping;

        public string ComponentName { get; set; } = string.Empty;

        public override void BeginInstance(ComponentPropertyTable table, int instance, uint version)
        {
            _inList.Clear();
            _inList.Push(false);
            json.WriteStartObject();
        }

        public override void EndInstance(ComponentPropertyTable table, int instance) => json.WriteEndObject();

        public override void Integer(ComponentPropertyDescriptor property, long value) => Leaf(property, ComponentPropertyValue.FromInteger(value));

        public override void Number(ComponentPropertyDescriptor property, double value) => Leaf(property, ComponentPropertyValue.FromNumber(value));

        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Leaf(property, ComponentPropertyValue.FromBoolean(value));

        public override void Text(ComponentPropertyDescriptor property, string? value) => Leaf(property, ComponentPropertyValue.FromText(value ?? string.Empty));

        public override void Enum(ComponentPropertyDescriptor property, long value) => Leaf(property, ComponentPropertyValue.FromEnum(value));

        public override void Guid(ComponentPropertyDescriptor property, Guid value) => Leaf(property, ComponentPropertyValue.FromGuid(value));

        public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Leaf(property, ComponentPropertyValue.FromColor(packedValue));

        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Leaf(property, ComponentPropertyValue.FromTag(value.IsNone ? string.Empty : value.Name));

        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) =>
            Leaf(property, value is null ? ComponentPropertyValue.Absent : ComponentPropertyValue.FromTagQuery(value));

        public override void Definition(ComponentPropertyDescriptor property, object? value) =>
            Leaf(property, value is null ? ComponentPropertyValue.Absent : ComponentPropertyValue.FromDefinition(value));

        public override void Absent(ComponentPropertyDescriptor property) => Leaf(property, ComponentPropertyValue.Absent);

        public override void BeginNested(ComponentPropertyDescriptor property) => Begin(property, isList: false);

        public override void EndNested(ComponentPropertyDescriptor property) => End(isList: false);

        public override void BeginIndexed(ComponentPropertyDescriptor property, int count) => Begin(property, isList: true);

        public override void EndIndexed(ComponentPropertyDescriptor property) => End(isList: true);

        private void Begin(ComponentPropertyDescriptor property, bool isList)
        {
            if (_skipping > 0 || !IsSaved(property))
            {
                _skipping++;
                return;
            }

            if (!_inList.Peek())
            {
                json.WritePropertyName(property.Name);
            }

            if (isList)
            {
                json.WriteStartArray();
            }
            else
            {
                json.WriteStartObject();
            }

            _inList.Push(isList);
        }

        private void End(bool isList)
        {
            if (_skipping > 0)
            {
                _skipping--;
                return;
            }

            _inList.Pop();
            if (isList)
            {
                json.WriteEndArray();
            }
            else
            {
                json.WriteEndObject();
            }
        }

        private void Leaf(ComponentPropertyDescriptor property, in ComponentPropertyValue value)
        {
            if (_skipping > 0 || !IsSaved(property))
            {
                return;
            }

            var inList = _inList.Peek();
            if (value.IsAbsent)
            {
                if (inList)
                {
                    json.WriteNullValue();
                }

                return;
            }

            if (!inList)
            {
                json.WritePropertyName(property.Name);
            }

            if (property.Meaning.Name is { } meaning)
            {
                if (!persistence.TryGetMeaning(meaning, out var meaningPersistence) || !meaningPersistence.TryWrite(json, property, value, capture))
                {
                    problems.Add($"{ComponentName}.{property.Name}: meaning {meaning} has no persistent form.");
                    json.WriteNullValue();
                }

                return;
            }

            if (property.Kind == ComponentPropertyKind.Definition)
            {
                if (!persistence.TryGetDefinition(property.ValueType, out var definitionPersistence))
                {
                    problems.Add($"{ComponentName}.{property.Name}: a {property.ValueType.Name} has no persistent form.");
                    json.WriteNullValue();
                    return;
                }

                definitionPersistence.Write(json, value.Reference!);
                return;
            }

            EntitySnapshotValues.Write(json, property, value);
        }
    }
}
