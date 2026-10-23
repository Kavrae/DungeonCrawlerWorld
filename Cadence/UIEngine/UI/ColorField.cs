using System.Globalization;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Content;

namespace UIEngine.UI;

/// <summary>
/// A TextBox holding a colour as hex -- RRGGBB, or RRGGBBAA with alpha -- with a swatch of it at the right edge. The
/// text is read when Enter submits it or the box loses focus; text that isn't a colour puts the last one back.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract; SetValue changes it afterwards without raising ValueChanged.</remarks>
public sealed class ColorField(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, CursorTextContent? cursorTextContent = null, ContextMenuController? contextMenuController = null)
    : TextBox(fontService, elementPoolService, renderService, cursorTextContent, contextMenuController)
{
    private const int SwatchInset = 2;

    public Color Value { get; private set; }

    /// <summary>Raised with the new colour when the player changes it. Not raised when the text read back to the same colour.</summary>
    public event Action<Color>? ValueChanged;

    public void Configure(Color value)
    {
        Value = value;
        TextSubmitted += _ => CommitText();
        FocusChanged += _ =>
        {
            if (!IsFocused)
            {
                CommitText();
            }
        };
    }

    public override void Initialize()
    {
        base.Initialize();

        SetText(Format(Value));
    }

    /// <summary>Shows value without raising ValueChanged -- the owner's own data changed.</summary>
    public void SetValue(Color value)
    {
        Value = value;
        SetText(Format(Value));
    }

    protected override bool AcceptsCharacter(char character) => char.IsAsciiHexDigit(character) || character == '#';

    public override void DrawContent(GameTime gameTime)
    {
        base.DrawContent(gameTime);

        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;
        var side = ContentRectangle.Height - SwatchInset * 2;
        var swatch = new Rectangle(ContentRectangle.Right - SwatchInset - side, ContentRectangle.Y + SwatchInset, side, side);
        spriteBatch.Draw(unitRectangle, swatch, WindowPalette.BorderColor);
        spriteBatch.Draw(unitRectangle, new Rectangle(swatch.X + 1, swatch.Y + 1, swatch.Width - 2, swatch.Height - 2), Value);
    }

    /// <summary>Reads RRGGBB or RRGGBBAA, with or without a leading #.</summary>
    public static bool TryParse(string text, out Color color)
    {
        var digits = text.StartsWith('#') ? text[1..] : text;
        if (digits.Length is not (6 or 8) || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var packed))
        {
            color = default;
            return false;
        }

        if (digits.Length == 6)
        {
            packed = (packed << 8) | 0xFF;
        }

        color = new Color((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    /// <summary>#RRGGBB for an opaque colour, #RRGGBBAA otherwise.</summary>
    public static string Format(Color color) =>
        color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private void CommitText()
    {
        if (TryParse(OriginalText, out var typed) && typed != Value)
        {
            Value = typed;
            ValueChanged?.Invoke(Value);
        }

        SetText(Format(Value));
    }
}
