using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// One line in an ability-score column's scrolling list ("Base : 6", "Some Source : +2",
/// "Another Source : -10%") -- right-aligned within its own bounds so the trailing numbers/
/// percentages line up regardless of source-name length. A plain Element (not Window), same
/// draw-only style as InventoryItemStackCell/AbilityScoreColumnHeader.
/// </summary>
public sealed class AbilityScoreModifierRow(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float FontFraction = 0.6f;

    private const float Padding = 3f;

    private static readonly Color TextColor = WindowPalette.BodyTextColor;

    private string _text = string.Empty;
    private SpriteFontBase _font = null!;

    public void Configure(string text, float rowHeight)
    {
        _text = text;
        _font = fontService.GetFont((int)(rowHeight * FontFraction));
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var footprintSize = new Vector2(ContentSize.X - Padding, ContentSize.Y);
        GlyphRenderer.DrawRightAligned(spriteBatch, _font, _text, ContentAbsolutePosition, footprintSize, TextColor);
    }
}
