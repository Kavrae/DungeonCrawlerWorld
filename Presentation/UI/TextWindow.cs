using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

public class TextWindow : Window
{
    public string OriginalText { get; set; } = string.Empty;
    public DisplayText DisplayText { get; set; }
    public SpriteFontBase ContentFont { get; set; }
    public Color TextColor { get; set; }
    private const int LinePadding = 3;

    public TextWindow(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer)
        : base(fontService, windowService, glyphRenderer)
    {
        ContentFont = fontService.GetFont(8);
    }

    public override void BuildWindow(Window? parentWindow, WindowOptions windowOptions)
    {
        base.BuildWindow(parentWindow, windowOptions);

        OriginalText = windowOptions.Text?.Text ?? string.Empty;
        TextColor = windowOptions.Text?.TextColor ?? Color.Black;
        _canContainChildWindows = false;
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!string.IsNullOrWhiteSpace(DisplayText.FormattedText))
        {
            spriteBatch.DrawString(ContentFont, DisplayText.FormattedText, new Vector2(LinePadding, LinePadding), TextColor);
        }
    }

    protected override void RecalculateFixedWindowSize()
    {
        base.RecalculateFixedWindowSize();

        ReformatDisplayText();
    }

    protected override void RecalculateFillWindowSize()
    {
        base.RecalculateFillWindowSize();

        ReformatDisplayText();
    }

    protected override void RecalculateWrapContentWindowSize()
    {
        _contentState.Size.X = _geometry.MaximumSize.X - _geometry.RelativePosition.X - BorderInsetDoubled.X;

        ReformatDisplayText();

        _contentState.Size.Y = ContentFont.LineHeight * DisplayText.LineCount + LinePadding * (DisplayText.LineCount + 1);

        _geometry.CurrentSize = _contentState.Size;
        if (_title.ShowTitle)
        {
            // Resize horizontally to fit the new content size, but keep the vertical size.
            _title.Size = new Vector2(_contentState.Size.X, _title.OriginalSize.Y - BorderInset.Y);
            _geometry.CurrentSize.Y += _title.Size.Y;
        }
        _geometry.CurrentSize += BorderInsetDoubled;
    }

    public void ReformatDisplayText()
    {
        DisplayText = StringUtility.FormatText(new FormatTextCriteria(
            new FontStashTextMeasurer(ContentFont),
            _contentState.Size.X - ContentPadding.X * 2,
            OriginalText,
            FormatTextMode.Wordwrap));
    }

    public void UpdateText(string newText)
    {
        OriginalText = newText;

        switch (_geometry.DisplayMode)
        {
            case WindowDisplayMode.Fixed:
                RecalculateFixedWindowSize();
                break;
            case WindowDisplayMode.Fill:
                RecalculateFillWindowSize();
                break;
            case WindowDisplayMode.WrapContent:
                RecalculateWrapContentWindowSize();
                break;
        }

        // Without this, a text-driven resize left the window's rectangles/absolute
        // positions/title button positions stale (still reflecting the size before the
        // text change) even though CurrentSize/ContentSize above were already correct.
        Arrange();
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        // TODO copy text to clipboard
    }
}
