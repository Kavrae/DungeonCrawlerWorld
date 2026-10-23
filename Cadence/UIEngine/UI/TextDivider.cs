using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>
/// A horizontal divider like SeparatorBar, but with a caller-positioned label interrupting the
/// line instead of no text support at all. The whole control spans widthFraction of its own
/// content width, centered (same centering idea as SeparatorBar.WidthFraction); textPosition
/// (0..1, a fraction of the *full* content width, not of that centered span) places the text's
/// left edge, with the divider line filling whatever's left on either side of it within the span.
/// textPosition 0 collapses the left segment away entirely (text starts right at the span's own
/// left edge). No default widthFraction/textPosition -- every caller states both explicitly via
/// Configure, since different call sites want different values (section headers at 90%/25%, say).
/// </summary>
public sealed class TextDivider(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private const float LineHeight = 1f;

    private string _text = string.Empty;
    private Color _color;
    private float _widthFraction;
    private float _textPosition;
    private SpriteFontBase _font = null!;

    /// <summary>The label's width in _font, measured when Configure sets them rather than every draw.</summary>
    private float _textWidth;

    // fontSize's default can't reference FontChrome.DefaultFontSize directly -- a default
    // parameter value must be a compile-time constant, and FontChrome's fields are plain mutable
    // statics (see its own doc comment) -- so it resolves inside the method body instead.
    public void Configure(string text, Color color, float widthFraction, float textPosition, int? fontSize = null)
    {
        _text = text;
        _color = color;
        _widthFraction = widthFraction;
        _textPosition = textPosition;
        _font = FontService.GetFont(fontSize ?? FontChrome.DefaultFontSize);
        _textWidth = _font.MeasureString(_text).X;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var margin = (1f - _widthFraction) / 2f * ContentSize.X;
        var textStart = margin + _textPosition * ContentSize.X;
        var textEnd = textStart + _textWidth;
        var rightEdge = ContentSize.X - margin;
        var lineY = ContentAbsolutePosition.Y + (ContentSize.Y - LineHeight) / 2f;

        // MeasureString's bounding box runs right up against the last glyph's own ink -- without
        // this gap the right-side line rendered as if it were touching/overlapping the text.
        var rightLineStart = textEnd + WindowChrome.Gap;

        if (textStart > margin)
        {
            DrawLine(margin, textStart - margin, lineY);
        }

        if (rightEdge > rightLineStart)
        {
            DrawLine(rightLineStart, rightEdge - rightLineStart, lineY);
        }

        // LineHeight, not the measured height -- MeasureString's bounding box tightens to whichever
        // glyphs this label has, so labels with and without descenders would sit at different heights.
        // The font's line height keeps every label's baseline in one place and leaves room for a
        // descender inside this row, short of a scrollable parent's clip boundary.
        var textY = ContentAbsolutePosition.Y + (ContentSize.Y - _font.LineHeight) / 2f;
        LabelRenderer.Draw(Render.SpriteBatch, _font, _text, new Vector2(ContentAbsolutePosition.X + textStart, textY), _color);
    }

    private void DrawLine(float startX, float width, float y)
    {
        var x = ContentAbsolutePosition.X + startX;
        Render.SpriteBatch.Draw(Render.UnitRectangle, new Rectangle((int)x, (int)y, (int)width, (int)LineHeight), _color);
    }
}
