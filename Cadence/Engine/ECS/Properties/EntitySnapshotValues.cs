using System.Globalization;
using System.Text.Json;
using Engine.Content;
using Engine.Tags;

namespace Engine.ECS.Properties;

/// <summary>A property value saved as itself, by its kind: numbers as numbers, an enum or flags by name, a Guid as content writes one, a tag by name, a tag query as its three sets of names.</summary>
/// <remarks>Names, not ids, for everything a session numbers on its own: an enum's member name survives reordering, and tag ids vary between runs. A definition is the game's to save (IComponentDefinitionPersistence).</remarks>
public static class EntitySnapshotValues
{
    private const string AllKey = "all";
    private const string AnyKey = "any";
    private const string NoneKey = "none";

    public static void Write(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value)
    {
        switch (property.Kind)
        {
            case ComponentPropertyKind.Number:
                json.WriteNumberValue(value.Number);
                break;
            case ComponentPropertyKind.Boolean:
                json.WriteBooleanValue(value.Boolean);
                break;
            case ComponentPropertyKind.Text or ComponentPropertyKind.Tag:
                json.WriteStringValue(value.Text);
                break;
            case ComponentPropertyKind.Enum or ComponentPropertyKind.Flags when property.ValueType.IsEnum:
                json.WriteStringValue(System.Enum.ToObject(property.ValueType, value.Integer).ToString());
                break;
            case ComponentPropertyKind.Guid:
                json.WriteStringValue(ContentGuid.Format(value.Guid));
                break;
            case ComponentPropertyKind.TagQuery when value.Reference is GameplayTagQuery query:
                WriteTagQuery(json, query);
                break;
            default:
                json.WriteNumberValue(value.Integer);
                break;
        }
    }

    public static bool TryRead(JsonElement element, ComponentPropertyDescriptor property, out ComponentPropertyValue value, out string? problem)
    {
        problem = null;
        value = default;
        switch (property.Kind)
        {
            case ComponentPropertyKind.Number when element.ValueKind == JsonValueKind.Number:
                value = ComponentPropertyValue.FromNumber(element.GetDouble());
                return true;
            case ComponentPropertyKind.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = ComponentPropertyValue.FromBoolean(element.GetBoolean());
                return true;
            case ComponentPropertyKind.Text when element.ValueKind == JsonValueKind.String:
                value = ComponentPropertyValue.FromText(element.GetString());
                return true;
            case ComponentPropertyKind.Tag when element.ValueKind == JsonValueKind.String:
                value = ComponentPropertyValue.FromTag(element.GetString() ?? string.Empty);
                return true;
            case ComponentPropertyKind.Enum or ComponentPropertyKind.Flags when property.ValueType.IsEnum && element.ValueKind == JsonValueKind.String:
                if (!System.Enum.TryParse(property.ValueType, element.GetString(), ignoreCase: false, out var member))
                {
                    problem = $"\"{element.GetString()}\" isn't a {property.ValueType.Name}";
                    return false;
                }

                value = ComponentPropertyValue.FromEnum(System.Convert.ToInt64(member, CultureInfo.InvariantCulture));
                return true;
            case ComponentPropertyKind.Guid when element.ValueKind == JsonValueKind.String:
                if (!ContentGuid.TryParse(element.GetString() ?? string.Empty, out var guid))
                {
                    problem = $"\"{element.GetString()}\" isn't a Guid";
                    return false;
                }

                value = ComponentPropertyValue.FromGuid(guid);
                return true;
            case ComponentPropertyKind.TagQuery when element.ValueKind == JsonValueKind.Object:
                if (!TryReadTagQuery(element, out var query, out problem))
                {
                    return false;
                }

                value = ComponentPropertyValue.FromTagQuery(query);
                return true;
            case ComponentPropertyKind.Integer or ComponentPropertyKind.Enum or ComponentPropertyKind.Flags or ComponentPropertyKind.Color
                when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var integer):
                value = ComponentPropertyValue.FromInteger(integer);
                return true;
            default:
                problem = $"a {property.Kind} can't be read from {element.ValueKind}";
                return false;
        }
    }

    private static void WriteTagQuery(Utf8JsonWriter json, GameplayTagQuery query)
    {
        json.WriteStartObject();
        WriteTagSet(json, AllKey, query.RequireAll);
        WriteTagSet(json, AnyKey, query.RequireAny);
        WriteTagSet(json, NoneKey, query.Exclude);
        json.WriteEndObject();
    }

    private static void WriteTagSet(Utf8JsonWriter json, string key, GameplayTagSet tags)
    {
        if (tags.IsEmpty)
        {
            return;
        }

        var names = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            names.Add(tag.Name);
        }

        names.Sort(StringComparer.Ordinal);
        json.WriteStartArray(key);
        foreach (var name in names)
        {
            json.WriteStringValue(name);
        }

        json.WriteEndArray();
    }

    private static bool TryReadTagQuery(JsonElement element, out GameplayTagQuery query, out string? problem)
    {
        query = null!;
        if (!TryReadTagSet(element, AllKey, out var all, out problem)
            || !TryReadTagSet(element, AnyKey, out var any, out problem)
            || !TryReadTagSet(element, NoneKey, out var none, out problem))
        {
            return false;
        }

        query = new GameplayTagQuery(all, any, none);
        return true;
    }

    private static bool TryReadTagSet(JsonElement element, string key, out GameplayTagSet tags, out string? problem)
    {
        tags = GameplayTagSet.Empty;
        problem = null;
        if (!element.TryGetProperty(key, out var names))
        {
            return true;
        }

        var read = new List<GameplayTag>();
        foreach (var name in names.EnumerateArray())
        {
            if (!GameplayTag.TryGet(name.GetString() ?? string.Empty, out var tag))
            {
                problem = $"tag \"{name.GetString()}\" isn't declared";
                return false;
            }

            read.Add(tag);
        }

        tags = GameplayTagSet.Create(read.ToArray());
        return true;
    }
}
