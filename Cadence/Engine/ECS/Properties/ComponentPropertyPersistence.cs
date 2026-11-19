using System.Text.Json;
using Engine.ECS.Systems;

namespace Engine.ECS.Properties;

/// <summary>How one meaning's values are saved: the persistent form a capture writes and a restore reads back.</summary>
public interface IComponentPropertyMeaningPersistence
{
    /// <summary>Writes value's persistent form as one JSON value; false when it has none, which fails the capture naming the property.</summary>
    bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture);

    /// <summary>The value a persistent form stands for in the session being restored into; false, with the reason, when it names nothing there.</summary>
    bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem);
}

/// <summary>How a definition held by a component is saved: through its content codec, as the game decides.</summary>
public interface IComponentDefinitionPersistence
{
    void Write(Utf8JsonWriter json, object definition);

    bool TryRead(JsonElement element, out object definition, out string? problem);
}

/// <summary>Every meaning and definition type a snapshot can save, by name and by type; a saved property whose meaning or definition type isn't here fails the snapshot check (EntitySnapshots.Check).</summary>
/// <remarks>Built per session: the game's persistent forms read its registries and catalogs. A meaning saved as stored names a value that means the same in any session (a duration, a part id, a Guid); a remapped one is issued fresh on restore (decision 17).</remarks>
public sealed class ComponentPropertyPersistence
{
    private readonly Dictionary<string, IComponentPropertyMeaningPersistence> _meanings = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IComponentDefinitionPersistence> _definitions = [];

    /// <summary>The engine's meanings: frames relative to the capture (decision 18), entity keys remapped within the snapshot (decision 17), and durations, tile counts and map positions as stored.</summary>
    public static ComponentPropertyPersistence WithEngineMeanings()
    {
        var persistence = new ComponentPropertyPersistence();
        persistence.Register(ComponentPropertyMeaningNames.Frame, new FramePersistence());
        persistence.Register(ComponentPropertyMeaningNames.EntityKey, new EntityKeyPersistence());
        persistence.RegisterAsStored(ComponentPropertyMeaningNames.Frames, ComponentPropertyMeaningNames.Tiles, ComponentPropertyMeaningNames.MapPosition);
        return persistence;
    }

    public void Register(string meaning, IComponentPropertyMeaningPersistence persistence) => _meanings[meaning] = persistence;

    /// <summary>Meanings whose values mean the same in any session, saved as the plain value.</summary>
    public void RegisterAsStored(params string[] meanings)
    {
        foreach (var meaning in meanings)
        {
            _meanings[meaning] = AsStoredPersistence.Instance;
        }
    }

    /// <summary>A meaning whose values are issued by a counter of this session (an item stack's id, a toggle's key): saved as the snapshot's own ordinals, each issued fresh on restore and every reference to it rewritten. <paramref name="noneValue"/> is the value that names nothing, kept as it is.</summary>
    public void RegisterRemapped(string meaning, Func<long> issue, long noneValue) => _meanings[meaning] = new RemappedPersistence(meaning, issue, noneValue);

    public void RegisterDefinition(Type definitionType, IComponentDefinitionPersistence persistence) => _definitions[definitionType] = persistence;

    public bool TryGetMeaning(string meaning, out IComponentPropertyMeaningPersistence persistence) => _meanings.TryGetValue(meaning, out persistence!);

    /// <summary>The persistence for a definition of declared type <paramref name="definitionType"/>: registered for that type or a base of it.</summary>
    public bool TryGetDefinition(Type definitionType, out IComponentDefinitionPersistence persistence)
    {
        for (var type = definitionType; type is not null; type = type.BaseType)
        {
            if (_definitions.TryGetValue(type, out persistence!))
            {
                return true;
            }
        }

        persistence = null!;
        return false;
    }

    /// <summary>A value saved as it is: its kind's own JSON (EntitySnapshotValues).</summary>
    private sealed class AsStoredPersistence : IComponentPropertyMeaningPersistence
    {
        public static readonly AsStoredPersistence Instance = new();

        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            EntitySnapshotValues.Write(json, property, in value);
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem) =>
            EntitySnapshotValues.TryRead(element, property, out value, out problem);
    }

    /// <summary>A frame saved relative to the capture frame, restored relative to the restore frame; never stays never, frame 0 -- a field's "not set" -- stays 0, and a past frame never goes below 0.</summary>
    private sealed class FramePersistence : IComponentPropertyMeaningPersistence
    {
        private const string NeverText = "never";
        private const string StartText = "start";

        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (value.Integer == FrameDeadline.Never)
            {
                json.WriteStringValue(NeverText);
                return true;
            }

            if (value.Integer == 0)
            {
                json.WriteStringValue(StartText);
                return true;
            }

            json.WriteNumberValue(value.Integer - capture.Frame);
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            problem = null;
            if (element.ValueKind == JsonValueKind.String && element.GetString() is NeverText or StartText)
            {
                value = ComponentPropertyValue.FromInteger(element.GetString() == NeverText ? FrameDeadline.Never : 0);
                return true;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var relative))
            {
                value = default;
                problem = $"a frame is a number of frames from the capture, \"{NeverText}\" or \"{StartText}\"";
                return false;
            }

            value = ComponentPropertyValue.FromInteger(System.Math.Clamp(restore.Frame + relative, 0, FrameDeadline.Never - 1));
            return true;
        }
    }

    /// <summary>An entity key saved as the position of its entity in the snapshot; a key naming an entity outside it, or none, is saved as null and restores as none.</summary>
    private sealed class EntityKeyPersistence : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (capture.TryGetEntityIndex(new Entities.EntityKey(unchecked((ulong)value.Integer)), out var index))
            {
                json.WriteNumberValue(index);
            }
            else
            {
                json.WriteNullValue();
            }

            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            problem = null;
            if (element.ValueKind == JsonValueKind.Null)
            {
                value = ComponentPropertyValue.FromInteger(0);
                return true;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var index) || !restore.TryGetEntityKey(index, out var key))
            {
                value = default;
                problem = "an entity key is the position of an entity in the snapshot, or null";
                return false;
            }

            value = ComponentPropertyValue.FromInteger(unchecked((long)key.Value));
            return true;
        }
    }

    /// <summary>A value issued by a session counter, saved as the snapshot's ordinal for it and issued fresh on restore.</summary>
    private sealed class RemappedPersistence(string meaning, Func<long> issue, long noneValue) : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (value.Integer == noneValue)
            {
                json.WriteNullValue();
            }
            else
            {
                json.WriteNumberValue(capture.OrdinalOf(meaning, value.Integer));
            }

            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            problem = null;
            if (element.ValueKind == JsonValueKind.Null)
            {
                value = ComponentPropertyValue.FromInteger(noneValue);
                return true;
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var ordinal))
            {
                value = default;
                problem = $"a {meaning} is the snapshot's ordinal for it, or null";
                return false;
            }

            value = ComponentPropertyValue.FromInteger(restore.Issued(meaning, ordinal, issue));
            return true;
        }
    }
}
