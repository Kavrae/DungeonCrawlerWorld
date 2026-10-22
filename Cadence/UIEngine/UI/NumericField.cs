using System.Globalization;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Content;

namespace UIEngine.UI;

/// <summary>
/// A TextBox holding a number between limits -- a whole number, or one with a fixed count of decimal places. Typing takes
/// only what a number is written with; the text is read when Enter submits it or the box loses focus, clamped to the
/// limits and rounded to the places shown, and text that isn't a number puts the last value back. The mouse wheel over
/// the box steps the value by its step, one step per notch.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract; SetValue changes it afterwards without raising ValueChanged.</remarks>
public sealed class NumericField(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, CursorTextContent? cursorTextContent = null, ContextMenuController? contextMenuController = null)
    : TextBox(fontService, elementPoolService, labelRenderer, cursorTextContent, contextMenuController)
{
    private double _minimum;
    private double _maximum;
    private double _step = 1;
    private int _decimalPlaces;

    public double Value { get; private set; }

    /// <summary>Raised with the new value when the player changes it -- by submitting text, leaving the box, or the wheel. Not raised when the text read back to the same value.</summary>
    public event Action<double>? ValueChanged;

    /// <param name="decimalPlaces">0 for a whole number.</param>
    public void Configure(double value, double minimum, double maximum, double step = 1, int decimalPlaces = 0)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, $"The minimum is above the maximum ({maximum}).");
        }

        _minimum = minimum;
        _maximum = maximum;
        _step = step;
        _decimalPlaces = decimalPlaces;
        Value = Normalize(value);
        TextSubmitted += _ => CommitTypedText();
        FocusChanged += _ =>
        {
            if (!IsFocused)
            {
                CommitTypedText();
            }
        };
    }

    public override void Initialize()
    {
        base.Initialize();

        SetText(Format(Value));
    }

    /// <summary>Shows value (clamped and rounded) without raising ValueChanged -- the owner's own data changed.</summary>
    public void SetValue(double value)
    {
        Value = Normalize(value);
        SetText(Format(Value));
    }

    protected override bool AcceptsCharacter(char character) =>
        char.IsAsciiDigit(character) || (character == '-' && _minimum < 0) || (character == '.' && _decimalPlaces > 0);

    protected override bool OnMouseWheelAction(int notches)
    {
        ChangeValue(Value + notches * _step);
        SetText(Format(Value));
        return true;
    }

    /// <summary>Takes what is typed as the value now (clamped and rounded; text that isn't a number shows the value again), as leaving the field or Enter does -- for an owner that reads Value on a press that doesn't move focus.</summary>
    public void CommitTypedText()
    {
        if (!double.TryParse(OriginalText, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
        {
            SetText(Format(Value));
            return;
        }

        ChangeValue(typed);
        SetText(Format(Value));
    }

    private void ChangeValue(double value)
    {
        var normalized = Normalize(value);
        if (normalized == Value)
        {
            return;
        }

        Value = normalized;
        ValueChanged?.Invoke(Value);
    }

    private double Normalize(double value) => System.Math.Clamp(System.Math.Round(value, _decimalPlaces, MidpointRounding.AwayFromZero), _minimum, _maximum);

    private string Format(double value) => value.ToString("F" + _decimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
