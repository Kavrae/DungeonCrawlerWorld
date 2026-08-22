using FontStashSharp;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// A horizontal divider like SeparatorBar, but with a caller-positioned label interrupting the
/// line instead of no text support at all. The whole control spans widthFraction of its own
/// content width, centered (same centering idea as SeparatorBar.WidthFraction); textPosition
/// (0..1, a fraction of the *full* content width, not of that centered span) places the text's
/// left edge, with the divider line filling whatever's left on either side of it within the span.
/// textPosition 0 collapses the left segment away entirely (text starts right at the span's own
/// left edge). No default widthFraction/textPosition -- every caller states both explicitly via
/// Configure, since different call sites want different values (e.g. ItemDetailsWindow's section
/// headers use 90%/25%).
/// </summary>
public sealed class TextDivider(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float LineHeight = 1f;

    private string _text = string.Empty;
    private Color _color;
    private float _widthFraction;
    private float _textPosition;
    private SpriteFontBase _font = null!;

    public void Configure(string text, Color color, float widthFraction, float textPosition, int fontSize = 12)
    {
        _text = text;
        _color = color;
        _widthFraction = widthFraction;
        _textPosition = textPosition;
        _font = FontService.GetFont(fontSize);
    }

    public override void DrawContent(GameTime gameTime)
    {
        var margin = (1f - _widthFraction) / 2f * ContentSize.X;
        var textStart = margin + _textPosition * ContentSize.X;
        var textSize = _font.MeasureString(_text);
        var textEnd = textStart + textSize.X;
        var rightEdge = ContentSize.X - margin;
        var lineY = ContentAbsolutePosition.Y + (ContentSize.Y - LineHeight) / 2f;

        if (textStart > margin)
        {
            DrawLine(margin, textStart - margin, lineY);
        }

        if (rightEdge > textEnd)
        {
            DrawLine(textEnd, rightEdge - textEnd, lineY);
        }

        var textY = ContentAbsolutePosition.Y + (ContentSize.Y - textSize.Y) / 2f;
        ElementPoolService.SpriteBatch.DrawString(_font, _text, new Vector2(ContentAbsolutePosition.X + textStart, textY), _color);
    }

    private void DrawLine(float startX, float width, float y)
    {
        var x = ContentAbsolutePosition.X + startX;
        ElementPoolService.SpriteBatch.Draw(ElementPoolService.UnitRectangle, new Rectangle((int)x, (int)y, (int)width, (int)LineHeight), _color);
    }
}
