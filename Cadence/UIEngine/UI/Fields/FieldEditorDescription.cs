using Microsoft.Xna.Framework;

namespace UIEngine.UI.Fields;

/// <summary>Which control a FieldRow edits its value with.</summary>
public enum FieldValueKind : byte
{
    /// <summary>A line of text (TextBox).</summary>
    Text,

    /// <summary>Several lines of text (a multi-line TextBox): Enter commits, Shift+Enter starts a new line.</summary>
    MultilineText,

    /// <summary>Text shown, never changed: a label on the row, not a control.</summary>
    ReadOnlyText,

    /// <summary>A whole number between limits (NumericField).</summary>
    Integer,

    /// <summary>A number with decimal places between limits (NumericField).</summary>
    Number,

    /// <summary>On or off (Toggle).</summary>
    Boolean,

    /// <summary>One of a short list of choices (DropdownField).</summary>
    Choice,

    /// <summary>One of a long list of choices, narrowed by typing (SearchableChoiceField); free text too when AllowsFreeText.</summary>
    SearchableChoice,

    /// <summary>Any number of a list of choices (MultiChoiceField).</summary>
    MultiChoice,

    /// <summary>A colour (ColorField).</summary>
    Color,
}

/// <summary>What one FieldRow shows and edits: its label and control, the limits or choices the control takes, and whether the value is set or only a default.</summary>
/// <remarks>Names nothing about where the value comes from -- a content record's field or a component's property map onto it the same way.</remarks>
public sealed record FieldEditorDescription(string Label, FieldValueKind Kind)
{
    /// <summary>How far the label is indented: one step per level of nesting.</summary>
    public int Depth { get; init; }

    public double Minimum { get; init; } = -1_000_000_000d;

    public double Maximum { get; init; } = 1_000_000_000d;

    /// <summary>For Number: how many decimal places are kept.</summary>
    public int DecimalPlaces { get; init; }

    /// <summary>What one wheel notch adds to a number.</summary>
    public double Step { get; init; } = 1d;

    /// <summary>For Choice, SearchableChoice and MultiChoice: the labels to choose from.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>For SearchableChoice: typed text that matches no choice is a value of its own.</summary>
    public bool AllowsFreeText { get; init; }

    /// <summary>False when the value shown is only the default an unset field means: the label is dimmed until something is chosen.</summary>
    public bool IsSet { get; init; } = true;

    /// <summary>A row that is only its label -- no control: the label takes the row's width, and the row's buttons follow it.</summary>
    public bool IsLabelOnly { get; init; }

    /// <summary>What the label's tooltip says beyond the problem and the hint, if anything: what an unset value means.</summary>
    public string? Tooltip { get; init; }

    /// <summary>What explains the value beyond its label -- its unit, what a special value means -- shown first in the label's tooltip, set or not.</summary>
    public string? Hint { get; init; }
}

/// <summary>A FieldRow's value, in the shape its kind takes: Text, Number, Boolean, the chosen index (with Text for free text), the chosen indices, or a Color.</summary>
public readonly record struct FieldValue(string Text = "", double Number = 0d, bool Boolean = false, int ChoiceIndex = -1, IReadOnlyList<int>? ChoiceIndices = null, Color Color = default)
{
    public static FieldValue OfText(string text) => new(Text: text);

    public static FieldValue OfNumber(double number) => new(Number: number);

    public static FieldValue OfBoolean(bool value) => new(Boolean: value);

    /// <summary>A chosen index; for free text, -1 with the typed text.</summary>
    public static FieldValue OfChoice(int index, string text = "") => new(Text: text, ChoiceIndex: index);

    public static FieldValue OfChoices(IReadOnlyList<int> indices) => new(ChoiceIndices: indices);

    public static FieldValue OfColor(Color color) => new(Color: color);
}
