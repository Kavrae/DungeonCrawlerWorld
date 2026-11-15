using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Content;
using Microsoft.Xna.Framework;
using UIEngine.UI.Fields;

namespace Workshop.Editing;

/// <summary>A record another one can name: its Guid and how a picker shows it.</summary>
public readonly record struct ContentReferenceChoice(Guid Id, string Label);

/// <summary>How one scalar content field maps onto a FieldRow: the row's kind, limits and choices, and its value both ways between the row and the record's JSON.</summary>
/// <remarks>The one place a ContentFieldKind meets a FieldValueKind, so the inspector and its tests agree on every conversion.</remarks>
public sealed class ContentFieldValues
{
    private const int NumberDecimalPlaces = 3;
    private const string MissingReferencePrefix = "(missing) ";

    private readonly ContentField _field;
    private readonly IReadOnlyList<string> _choiceLabels;
    private readonly IReadOnlyList<ContentReferenceChoice> _references;

    private ContentFieldValues(ContentField field, FieldValueKind kind, IReadOnlyList<string> choiceLabels, IReadOnlyList<ContentReferenceChoice> references)
    {
        _field = field;
        Kind = kind;
        _choiceLabels = choiceLabels;
        _references = references;
    }

    /// <summary>A flags enum: chosen as several members, written as their names joined by ", " (Enum.ToString's form, which the reader takes).</summary>
    private bool IsFlags => Kind == FieldValueKind.MultiChoice && _field.ValueType is { IsEnum: true } enumType && IsFlagsEnum(enumType);

    private static bool IsFlagsEnum(Type enumType) => enumType.IsDefined(typeof(FlagsAttribute), inherit: false);

    /// <summary>A flags enum's members that are a flag (not zero), in value order.</summary>
    private static string[] FlagNames(Type enumType) =>
        [.. Enum.GetValues(enumType).Cast<Enum>().Where(member => Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture) != 0).Select(member => member.ToString())];

    private static IEnumerable<string> SplitFlags(string text) => text.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0);

    public FieldValueKind Kind { get; }

    public IReadOnlyList<string> ChoiceLabels => _choiceLabels;

    /// <param name="kind">The value's own kind: the field's, or for a list its elements'.</param>
    /// <param name="textChoices">For Text: what a picker offers (free text still allowed); null for plain text.</param>
    /// <param name="references">For Reference: the records of the field's table.</param>
    /// <param name="tagNames">For Tag and TagSet: the tags the field's domain accepts.</param>
    public static ContentFieldValues For(ContentField field, ContentFieldKind kind, IReadOnlyList<string>? textChoices, IReadOnlyList<ContentReferenceChoice> references, IReadOnlyList<string> tagNames) => kind switch
    {
        ContentFieldKind.Text when textChoices is not null => new(field, FieldValueKind.SearchableChoice, textChoices, []),
        ContentFieldKind.Text when field.IsMultiline && field.Kind == ContentFieldKind.Text => new(field, FieldValueKind.MultilineText, [], []),
        ContentFieldKind.Text => new(field, FieldValueKind.Text, [], []),
        ContentFieldKind.Integer => new(field, FieldValueKind.Integer, [], []),
        ContentFieldKind.Number => new(field, FieldValueKind.Number, [], []),
        ContentFieldKind.Boolean => new(field, FieldValueKind.Boolean, [], []),
        ContentFieldKind.Enum when field.ValueType is { IsEnum: true } flagsType && IsFlagsEnum(flagsType) => new(field, FieldValueKind.MultiChoice, FlagNames(flagsType), []),
        ContentFieldKind.Enum => new(field, FieldValueKind.Choice, field.ValueType is { IsEnum: true } enumType ? Enum.GetNames(enumType) : [], []),
        ContentFieldKind.Color => new(field, FieldValueKind.Color, [], []),
        ContentFieldKind.Reference => new(field, FieldValueKind.SearchableChoice, [.. references.Select(reference => reference.Label)], references),
        ContentFieldKind.Tag => new(field, FieldValueKind.SearchableChoice, tagNames, []),
        ContentFieldKind.TagSet => new(field, FieldValueKind.MultiChoice, tagNames, []),
        _ => new(field, FieldValueKind.ReadOnlyText, [], []),
    };

    /// <summary>The row's description for this field at depth, labelled label -- less a trailing "(hint)", which goes to the tooltip -- set or not.</summary>
    public FieldEditorDescription Describe(string label, int depth, bool isSet, IReadOnlyList<string>? choicesOverride = null)
    {
        var isWholeNumber = Kind == FieldValueKind.Integer;
        var minimum = _field.Minimum ?? (isWholeNumber ? int.MinValue : -1_000_000d);
        var maximum = _field.Maximum ?? (isWholeNumber ? int.MaxValue : 1_000_000d);
        var (shownLabel, hint) = ContentFieldLabels.Split(label);
        return new FieldEditorDescription(shownLabel, Kind)
        {
            Hint = hint,
            Depth = depth,
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = isWholeNumber ? 0 : NumberDecimalPlaces,
            Step = !isWholeNumber && maximum - minimum <= 1d ? 0.05d : 1d,
            Choices = choicesOverride ?? _choiceLabels,
            AllowsFreeText = _field.Kind == ContentFieldKind.Text || (_field.Kind == ContentFieldKind.List && _field.ElementKind == ContentFieldKind.Text),
            IsSet = isSet,
            Tooltip = isSet ? null : $"Unset: {ContentSchema.DescribeDefault(_field.Default)}",
        };
    }

    /// <summary>Whether json is a value this field's row can show; one that isn't is shown as written, and flagged.</summary>
    public bool CanShow(JsonElement json) => IsFlags ? json.ValueKind == JsonValueKind.String : Kind switch
    {
        FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.ReadOnlyText => json.ValueKind == JsonValueKind.String,
        FieldValueKind.Integer => json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out _),
        FieldValueKind.Number => json.ValueKind == JsonValueKind.Number,
        FieldValueKind.Boolean => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FieldValueKind.Color => json.ValueKind == JsonValueKind.Number && json.TryGetUInt32(out _),
        FieldValueKind.MultiChoice => json.ValueKind == JsonValueKind.Array && json.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String),
        _ => json.ValueKind == JsonValueKind.String,
    };

    /// <summary>The choices a row offers for json: the field's own, plus the Guid json names when it names a record that isn't there, so it can still be shown.</summary>
    public IReadOnlyList<string> ChoicesFor(JsonElement? json) =>
        _references.Count > 0 && json is { ValueKind: JsonValueKind.String } value && ContentGuid.TryParse(value.GetString()!, out var id) && !_references.Any(reference => reference.Id == id)
            ? [.. _choiceLabels, MissingReferencePrefix + value.GetString()]
            : _choiceLabels;

    /// <summary>The row's value for json, or for the field's default when json is null.</summary>
    public FieldValue ValueOf(JsonElement? json)
    {
        if (json is not { } value)
        {
            return DefaultValue();
        }

        if (IsFlags)
        {
            return FlagsValueOf(value.GetString() ?? string.Empty);
        }

        return Kind switch
        {
            FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.ReadOnlyText => FieldValue.OfText(value.GetString() ?? string.Empty),
            FieldValueKind.Integer => FieldValue.OfNumber(value.GetInt64()),
            FieldValueKind.Number => FieldValue.OfNumber(value.GetDouble()),
            FieldValueKind.Boolean => FieldValue.OfBoolean(value.GetBoolean()),
            FieldValueKind.Color => FieldValue.OfColor(new Color { PackedValue = value.GetUInt32() }),
            FieldValueKind.MultiChoice => FieldValue.OfChoices([.. value.EnumerateArray().Select(element => IndexOfLabel(element.GetString()!)).Where(index => index >= 0)]),
            _ => ChoiceValueOf(value.GetString()!),
        };
    }

    private FieldValue ChoiceValueOf(string text)
    {
        if (_references.Count > 0 && ContentGuid.TryParse(text, out var id))
        {
            for (var index = 0; index < _references.Count; index++)
            {
                if (_references[index].Id == id)
                {
                    return FieldValue.OfChoice(index, _references[index].Label);
                }
            }

            return FieldValue.OfChoice(_references.Count, MissingReferencePrefix + text);
        }

        var labelIndex = IndexOfLabel(text);
        return FieldValue.OfChoice(labelIndex, text);
    }

    private int IndexOfLabel(string label)
    {
        for (var index = 0; index < _choiceLabels.Count; index++)
        {
            if (string.Equals(_choiceLabels[index], label, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private FieldValue FlagsValueOf(string text) => FieldValue.OfChoices([.. SplitFlags(text).Select(IndexOfLabel).Where(index => index >= 0)]);

    /// <summary>The row's value for a field left out: its default, except a required one's, which has no value until one is chosen ("Select...").</summary>
    private FieldValue DefaultValue() => (_field.IsRequired ? null : _field.Default) switch
    {
        Enum flags when IsFlags => FlagsValueOf(flags.ToString()),
        string text when Kind == FieldValueKind.SearchableChoice => FieldValue.OfChoice(IndexOfLabel(text), text),
        string text => FieldValue.OfText(text),
        long number when Kind == FieldValueKind.Color => FieldValue.OfColor(new Color { PackedValue = (uint)number }),
        long number => FieldValue.OfNumber(number),
        float number => FieldValue.OfNumber(number),
        bool flag => FieldValue.OfBoolean(flag),
        Enum member => FieldValue.OfChoice(IndexOfLabel(member.ToString())),
        _ => Kind switch
        {
            FieldValueKind.MultiChoice => FieldValue.OfChoices([]),
            FieldValueKind.Choice or FieldValueKind.SearchableChoice => FieldValue.OfChoice(-1),
            _ => default,
        },
    };

    /// <summary>The JSON a committed row value is written as; null when it names nothing that can be written (a missing record still chosen).</summary>
    public JsonNode? JsonOf(FieldValue value) => IsFlags ? FlagsJsonOf(value) : Kind switch
    {
        FieldValueKind.Text or FieldValueKind.MultilineText => JsonValue.Create(value.Text),
        FieldValueKind.Integer => JsonValue.Create((long)System.Math.Round(value.Number, MidpointRounding.AwayFromZero)),
        FieldValueKind.Number => JsonValue.Create((float)value.Number),
        FieldValueKind.Boolean => JsonValue.Create(value.Boolean),
        FieldValueKind.Color => JsonValue.Create(value.Color.PackedValue),
        FieldValueKind.MultiChoice => new JsonArray([.. (value.ChoiceIndices ?? []).Select(index => _choiceLabels[index]).Order(StringComparer.Ordinal).Select(name => (JsonNode)JsonValue.Create(name)!)]),
        FieldValueKind.SearchableChoice when _references.Count > 0 => value.ChoiceIndex >= 0 && value.ChoiceIndex < _references.Count ? JsonValue.Create(ContentGuid.Format(_references[value.ChoiceIndex].Id)) : null,
        FieldValueKind.SearchableChoice or FieldValueKind.Choice => value.ChoiceIndex >= 0 && value.ChoiceIndex < _choiceLabels.Count ? JsonValue.Create(_choiceLabels[value.ChoiceIndex]) : _field.Kind == ContentFieldKind.Text || _field.ElementKind == ContentFieldKind.Text ? JsonValue.Create(value.Text) : null,
        _ => null,
    };

    /// <summary>The chosen flags' names in value order, joined as Enum.ToString joins them; the zero member's name, or nothing, when none is chosen.</summary>
    private JsonNode? FlagsJsonOf(FieldValue value)
    {
        var chosen = (value.ChoiceIndices ?? []).Order().Select(index => _choiceLabels[index]).ToList();
        if (chosen.Count > 0)
        {
            return JsonValue.Create(string.Join(", ", chosen));
        }

        return Enum.GetName(_field.ValueType!, 0) is { } zeroName ? JsonValue.Create(zeroName) : null;
    }
}
