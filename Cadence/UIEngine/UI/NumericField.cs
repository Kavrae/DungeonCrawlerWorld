using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Content;

namespace UIEngine.UI;

/// <summary>
/// A TextBox holding a number between limits -- a whole number, or one with a fixed count of decimal places. Typing takes
/// only what a number is written with; the text is read when Enter submits it or the box loses focus, clamped to the
/// limits and rounded to the places shown, and text that isn't a number puts the last value back. The mouse wheel over
/// the box and, focused, Up and Down step the value by its step (Up/Down repeating while held); Home sets the minimum and
/// End the maximum (with Shift they select text as in any box). Optional spinner buttons at the right edge step it too.
/// </summary>
/// <remarks>
/// A step starts from what is typed, as Enter would read it. Configure before Initialize, the usual contract; SetValue
/// changes it afterwards without raising ValueChanged.
/// </remarks>
public sealed class NumericField(ElementServices services, CursorTextContent? cursorTextContent = null, ContextMenuController? contextMenuController = null)
    : TextBox(services, cursorTextContent, contextMenuController)
{
    /// <summary>How wide the spinner buttons are, when shown.</summary>
    public const float SpinnerWidth = 14f;

    private const int SpinnerArrowWidth = 7;

    private double _minimum;
    private double _maximum;
    private double _step = 1;
    private int _decimalPlaces;
    private bool _showsSpinnerButtons;

    public double Value { get; private set; }

    /// <summary>Whether the up and down spinner buttons are drawn at the right edge.</summary>
    public bool ShowsSpinnerButtons => _showsSpinnerButtons;

    /// <summary>Raised with the new value when the player changes it -- by submitting text, leaving the box, the wheel, a key or a spinner button. Not raised when the text read back to the same value.</summary>
    public event Action<double>? ValueChanged;

    /// <param name="decimalPlaces">0 for a whole number.</param>
    /// <param name="showsSpinnerButtons">Whether up and down buttons are drawn at the right edge to step the value.</param>
    public void Configure(double value, double minimum, double maximum, double step = 1, int decimalPlaces = 0, bool showsSpinnerButtons = false)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, $"The minimum is above the maximum ({maximum}).");
        }

        _minimum = minimum;
        _maximum = maximum;
        _step = step;
        _decimalPlaces = decimalPlaces;
        _showsSpinnerButtons = showsSpinnerButtons;
        Value = Normalize(value);
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _showsSpinnerButtons = false;
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

    protected override float TrailingReservedWidth => _showsSpinnerButtons ? SpinnerWidth : 0f;

    protected override bool AcceptsCharacter(char character) =>
        char.IsAsciiDigit(character) || (character == '-' && _minimum < 0) || (character == '.' && _decimalPlaces > 0);

    protected override bool OnMouseWheelAction(int notches)
    {
        if (!AcceptsEdits)
        {
            return false;
        }

        StepBy(notches);
        return true;
    }

    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        var shiftHeld = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
        if (AcceptsEdits && !shiftHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.Home))
        {
            SetValueFromKey(_minimum);
            return;
        }

        if (AcceptsEdits && !shiftHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.End))
        {
            SetValueFromKey(_maximum);
            return;
        }

        if (ShouldFire(Keys.Up, keyboardState) && AcceptsEdits)
        {
            StepBy(1);
        }

        if (ShouldFire(Keys.Down, keyboardState) && AcceptsEdits)
        {
            StepBy(-1);
        }

        base.OnHotkeysAction(keyboardState, previousKeyboardState);
    }

    /// <summary>A press on a spinner button steps the value; anywhere else places the caret as in any box.</summary>
    protected override bool OnContentClickAction(Point mousePosition)
    {
        var localPosition = ToContentLocal(new Vector2(mousePosition.X, mousePosition.Y));
        if (_showsSpinnerButtons && AcceptsEdits && SpinnerRectangle().Contains((int)localPosition.X, (int)localPosition.Y))
        {
            StepBy(localPosition.Y < SpinnerRectangle().Center.Y ? 1 : -1);
            return true;
        }

        return base.OnContentClickAction(mousePosition);
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

    /// <summary>Reads what is typed, then moves the value steps steps (negative for down), held within the limits.</summary>
    private void StepBy(int steps)
    {
        CommitTypedText();
        ChangeValue(Value + steps * _step);
        SetText(Format(Value));
    }

    private void SetValueFromKey(double value)
    {
        ChangeValue(value);
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

    /// <summary>The spinner buttons' area at the content's right edge, in local coordinates: the up button its top half, the down button its bottom.</summary>
    private Rectangle SpinnerRectangle()
    {
        var content = LocalContentRectangle;
        return new Rectangle(content.Right - (int)SpinnerWidth, content.Y, (int)SpinnerWidth, content.Height);
    }

    public override void DrawContent(GameTime gameTime)
    {
        base.DrawContent(gameTime);

        if (!_showsSpinnerButtons)
        {
            return;
        }

        var spinner = SpinnerRectangle();
        var arrowColor = AcceptsEdits ? WindowPalette.BodyTextColor : WindowPalette.BodyTextColor * 0.45f;
        Render.Batch.FillRectangle(spinner, WindowPalette.ControlBackground);
        Render.Batch.FillRectangle(new Rectangle(spinner.X, spinner.Y, 1, spinner.Height), WindowPalette.BorderColor);
        Render.Batch.FillRectangle(new Rectangle(spinner.X, spinner.Center.Y, spinner.Width, 1), WindowPalette.BorderColor);
        DrawArrow(spinner, pointsUp: true, arrowColor);
        DrawArrow(spinner, pointsUp: false, arrowColor);
    }

    /// <summary>A triangle of shrinking rows centred in its half of the spinner -- drawn, since the font has no arrow glyph.</summary>
    private void DrawArrow(Rectangle spinner, bool pointsUp, Color color)
    {
        var rows = (SpinnerArrowWidth + 1) / 2;
        var halfHeight = spinner.Height / 2;
        var halfTop = pointsUp ? spinner.Y : spinner.Y + halfHeight;
        var top = halfTop + (halfHeight - rows) / 2;
        var left = spinner.X + (spinner.Width - SpinnerArrowWidth) / 2 + 1;
        for (var row = 0; row < rows; row++)
        {
            var rowFromWidest = pointsUp ? rows - 1 - row : row;
            Render.Batch.FillRectangle(new Rectangle(left + rowFromWidest, top + row, SpinnerArrowWidth - rowFromWidest * 2, 1), color);
        }
    }
}
