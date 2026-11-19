using System.Globalization;
using Engine.ECS.Properties;
using Microsoft.Xna.Framework;

namespace UIEngine.UI.Fields;

/// <summary>Maps a component property onto a FieldRow -- which control edits it, its limits or choices, the value it shows -- and a committed field value back onto the property value an edit writes.</summary>
/// <remarks>
/// The component side of the field editors the content inspector shares: a number edits as a number field limited to its
/// type, an enum as a choice, flags as several, a value whose meaning offers choices (a blueprint, an aura, an item) as a
/// searchable choice, and anything read-only, derived, a definition or a tag query as text that can't take focus.
/// </remarks>
public static class ComponentPropertyFields
{
    /// <summary>What an absent optional is called among an enum's choices.</summary>
    public const string NoneChoice = "none";

    private const double LargestExactInteger = 9_007_199_254_740_991d;

    /// <summary>The row for one property: its control, limits and choices; read-only text when it can't be edited.</summary>
    /// <param name="shownValue">The value as inspection shows it (resolved through its meaning), for a read-only row and as the hint of an edited one.</param>
    /// <param name="choices">The values the property's meaning offers to pick from, or null.</param>
    public static FieldEditorDescription Describe(ComponentPropertyDescriptor? property, string label, int depth, string shownValue, ComponentPropertyValue raw, bool isEditable, bool isHeading, IReadOnlyList<ComponentPropertyChoice>? choices)
    {
        if (isHeading)
        {
            return new FieldEditorDescription(label, FieldValueKind.ReadOnlyText) { Depth = depth, IsLabelOnly = true };
        }

        var kind = property is null || !isEditable ? FieldValueKind.ReadOnlyText : KindOf(property, raw, choices);
        var description = new FieldEditorDescription(label, kind) { Depth = depth };
        if (kind == FieldValueKind.ReadOnlyText)
        {
            return description with { IsSet = isEditable || property is null, Hint = isEditable ? null : "Read-only" };
        }

        var hint = shownValue != RawText(raw, property?.ValueType) ? shownValue : null;
        return kind switch
        {
            FieldValueKind.SearchableChoice => description with { Choices = choices!.Select(static choice => choice.Label).ToList(), Hint = hint },
            FieldValueKind.Integer => WithIntegerLimits(description, property!.ValueType) with { Hint = hint },
            FieldValueKind.Number => description with { DecimalPlaces = 3, Minimum = -LargestExactInteger, Maximum = LargestExactInteger, Hint = hint },
            FieldValueKind.Choice => description with { Choices = EnumChoices(property!) },
            FieldValueKind.MultiChoice => description with { Choices = FlagNames(property!.ValueType) },
            _ => description with { Hint = hint },
        };
    }

    /// <summary>The value a row described by <paramref name="description"/> shows.</summary>
    public static FieldValue ValueOf(FieldEditorDescription description, ComponentPropertyDescriptor? property, string shownValue, ComponentPropertyValue raw, IReadOnlyList<ComponentPropertyChoice>? choices)
    {
        switch (description.Kind)
        {
            case FieldValueKind.SearchableChoice:
                return FieldValue.OfChoice(IndexOf(choices!, raw));
            case FieldValueKind.Integer:
                return FieldValue.OfNumber(raw.Integer);
            case FieldValueKind.Number:
                return FieldValue.OfNumber(raw.Number);
            case FieldValueKind.Boolean:
                return FieldValue.OfBoolean(raw.Boolean);
            case FieldValueKind.Choice:
                return FieldValue.OfChoice(EnumIndexOf(property!, raw));
            case FieldValueKind.MultiChoice:
                return FieldValue.OfChoices(FlagIndicesOf(property!.ValueType, raw.Integer));
            case FieldValueKind.Color:
                return FieldValue.OfColor(new Color { PackedValue = raw.PackedColor });
            case FieldValueKind.Text:
                return FieldValue.OfText(RawText(raw, property?.ValueType));
            default:
                return FieldValue.OfText(shownValue);
        }
    }

    /// <summary>The property value a committed field value writes; false when the field holds nothing writable (no choice made, text that isn't a Guid).</summary>
    public static bool TryRead(FieldEditorDescription description, ComponentPropertyDescriptor property, FieldValue value, IReadOnlyList<ComponentPropertyChoice>? choices, out ComponentPropertyValue written)
    {
        written = default;
        switch (description.Kind)
        {
            case FieldValueKind.SearchableChoice when choices is not null && value.ChoiceIndex >= 0 && value.ChoiceIndex < choices.Count:
                written = choices[value.ChoiceIndex].Value;
                return true;
            case FieldValueKind.Integer:
                written = ComponentPropertyValue.FromInteger((long)System.Math.Round(value.Number));
                return true;
            case FieldValueKind.Number:
                written = ComponentPropertyValue.FromNumber(value.Number);
                return true;
            case FieldValueKind.Boolean:
                written = ComponentPropertyValue.FromBoolean(value.Boolean);
                return true;
            case FieldValueKind.Choice:
                return TryReadEnum(property, value.ChoiceIndex, out written);
            case FieldValueKind.MultiChoice:
                written = ComponentPropertyValue.FromEnum(FlagsOf(property.ValueType, value.ChoiceIndices ?? []));
                return true;
            case FieldValueKind.Color:
                written = ComponentPropertyValue.FromColor(value.Color.PackedValue);
                return true;
            case FieldValueKind.Text when property.Kind == ComponentPropertyKind.Guid:
                if (!System.Guid.TryParse(value.Text, out var guid))
                {
                    return false;
                }

                written = ComponentPropertyValue.FromGuid(guid);
                return true;
            case FieldValueKind.Text when property.Kind == ComponentPropertyKind.Tag:
                written = ComponentPropertyValue.FromTag(value.Text);
                return true;
            case FieldValueKind.Text when property.Kind == ComponentPropertyKind.Integer:
                return TryReadWideInteger(property.ValueType, value.Text, out written);
            case FieldValueKind.Text:
                written = ComponentPropertyValue.FromText(value.Text);
                return true;
            default:
                return false;
        }
    }

    private static FieldValueKind KindOf(ComponentPropertyDescriptor property, ComponentPropertyValue raw, IReadOnlyList<ComponentPropertyChoice>? choices)
    {
        if (raw.IsAbsent && property.Kind != ComponentPropertyKind.Enum)
        {
            return FieldValueKind.ReadOnlyText;
        }

        if (choices is not null && property.Kind is ComponentPropertyKind.Integer or ComponentPropertyKind.Guid)
        {
            return FieldValueKind.SearchableChoice;
        }

        return property.Kind switch
        {
            ComponentPropertyKind.Integer when IsWideInteger(property.ValueType) => FieldValueKind.Text,
            ComponentPropertyKind.Integer => FieldValueKind.Integer,
            ComponentPropertyKind.Number => FieldValueKind.Number,
            ComponentPropertyKind.Boolean => FieldValueKind.Boolean,
            ComponentPropertyKind.Text or ComponentPropertyKind.Guid or ComponentPropertyKind.Tag => FieldValueKind.Text,
            ComponentPropertyKind.Enum => FieldValueKind.Choice,
            ComponentPropertyKind.Flags => FieldValueKind.MultiChoice,
            ComponentPropertyKind.Color => FieldValueKind.Color,
            _ => FieldValueKind.ReadOnlyText,
        };
    }

    private static FieldEditorDescription WithIntegerLimits(FieldEditorDescription description, Type valueType)
    {
        var (minimum, maximum) = Type.GetTypeCode(valueType) switch
        {
            TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
            TypeCode.Byte => (byte.MinValue, byte.MaxValue),
            TypeCode.Int16 => (short.MinValue, short.MaxValue),
            TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
            TypeCode.Int32 => (int.MinValue, int.MaxValue),
            TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
            _ => (0d, LargestExactInteger),
        };

        return description with { Minimum = minimum, Maximum = maximum };
    }

    /// <summary>A 64-bit integer, which a number field (a double) can't hold exactly past 2^53, so it is edited as text.</summary>
    private static bool IsWideInteger(Type valueType) => Type.GetTypeCode(valueType) is TypeCode.Int64 or TypeCode.UInt64;

    /// <summary>A wide integer typed as text, parsed exactly as its type takes it (ComponentPropertyIntegers): a ulong up to ulong.MaxValue, carried as its bits.</summary>
    private static bool TryReadWideInteger(Type valueType, string? text, out ComponentPropertyValue written)
    {
        written = default;
        if (!ComponentPropertyIntegers.TryParse(valueType, text, out var parsed))
        {
            return false;
        }

        written = ComponentPropertyValue.FromInteger(parsed);
        return true;
    }

    private static string RawText(ComponentPropertyValue raw, Type? valueType) => raw.IsAbsent ? NoneChoice : raw.Kind switch
    {
        ComponentPropertyKind.Number => raw.Number.ToString("0.###", CultureInfo.InvariantCulture),
        ComponentPropertyKind.Boolean => raw.Boolean ? "true" : "false",
        ComponentPropertyKind.Guid => raw.Guid.ToString(),
        ComponentPropertyKind.Text or ComponentPropertyKind.Tag => raw.Text ?? string.Empty,
        _ => valueType is null ? raw.Integer.ToString(CultureInfo.InvariantCulture) : ComponentPropertyIntegers.Format(valueType, raw.Integer),
    };

    private static int IndexOf(IReadOnlyList<ComponentPropertyChoice> choices, ComponentPropertyValue raw)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index].Value;
            if (choice.Kind == ComponentPropertyKind.Guid ? choice.Guid == raw.Guid : choice.Integer == raw.Integer)
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> EnumChoices(ComponentPropertyDescriptor property)
    {
        var names = new List<string>();
        if (property.IsOptional)
        {
            names.Add(NoneChoice);
        }

        names.AddRange(System.Enum.GetNames(property.ValueType));
        return names;
    }

    private static int EnumIndexOf(ComponentPropertyDescriptor property, ComponentPropertyValue raw)
    {
        var offset = property.IsOptional ? 1 : 0;
        if (raw.IsAbsent)
        {
            return property.IsOptional ? 0 : -1;
        }

        var values = System.Enum.GetValuesAsUnderlyingType(property.ValueType);
        for (var index = 0; index < values.Length; index++)
        {
            if (Convert.ToInt64(values.GetValue(index), CultureInfo.InvariantCulture) == raw.Integer)
            {
                return index + offset;
            }
        }

        return -1;
    }

    private static bool TryReadEnum(ComponentPropertyDescriptor property, int choiceIndex, out ComponentPropertyValue written)
    {
        written = default;
        if (property.IsOptional && choiceIndex == 0)
        {
            written = ComponentPropertyValue.Absent;
            return true;
        }

        var values = System.Enum.GetValuesAsUnderlyingType(property.ValueType);
        var index = choiceIndex - (property.IsOptional ? 1 : 0);
        if (index < 0 || index >= values.Length)
        {
            return false;
        }

        written = ComponentPropertyValue.FromEnum(Convert.ToInt64(values.GetValue(index), CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>A flags enum's single-bit members, by name: what each choice of a multi-choice is.</summary>
    private static List<(string Name, long Bit)> FlagBits(Type flagsType)
    {
        var bits = new List<(string, long)>();
        foreach (var name in System.Enum.GetNames(flagsType))
        {
            var bit = Convert.ToInt64(System.Enum.Parse(flagsType, name), CultureInfo.InvariantCulture);
            if (bit != 0 && (bit & (bit - 1)) == 0)
            {
                bits.Add((name, bit));
            }
        }

        return bits;
    }

    private static List<string> FlagNames(Type flagsType) => FlagBits(flagsType).Select(static bit => bit.Name).ToList();

    private static List<int> FlagIndicesOf(Type flagsType, long value)
    {
        var bits = FlagBits(flagsType);
        var indices = new List<int>();
        for (var index = 0; index < bits.Count; index++)
        {
            if ((value & bits[index].Bit) != 0)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    private static long FlagsOf(Type flagsType, IReadOnlyList<int> chosen)
    {
        var bits = FlagBits(flagsType);
        long value = 0;
        foreach (var index in chosen)
        {
            if (index >= 0 && index < bits.Count)
            {
                value |= bits[index].Bit;
            }
        }

        return value;
    }
}
