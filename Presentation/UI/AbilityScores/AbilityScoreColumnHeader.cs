using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// The non-scrolling header at the top of one ability-score column: the score's name centered
/// on one line, its Total centered on the line below. A plain Element (not Window) -- same
/// draw-only reasoning as InventoryItemStackCell: no title/chrome needed, just draws itself.
/// </summary>
public sealed class AbilityScoreColumnHeader(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float NameFontFraction = 0.35f;
    private const float TotalFontFraction = 0.3f;

    private static readonly Color TextColor = WindowPalette.BodyTextColor;

    private string _name = string.Empty;
    private string _totalText = string.Empty;
    private SpriteFontBase _nameFont = null!;
    private SpriteFontBase _totalFont = null!;

    public void Configure(string name, short total, Vector2 headerSize)
    {
        _name = name;
        _totalText = total.ToString();
        _nameFont = fontService.GetFont((int)(headerSize.Y * NameFontFraction));
        _totalFont = fontService.GetFont((int)(headerSize.Y * TotalFontFraction));
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var stripSize = new Vector2(ContentSize.X, ContentSize.Y / 2f);

        GlyphRenderer.DrawCentered(spriteBatch, _nameFont, _name, ContentAbsolutePosition, stripSize, TextColor);
        GlyphRenderer.DrawCentered(spriteBatch, _totalFont, _totalText, ContentAbsolutePosition + new Vector2(0, stripSize.Y), stripSize, TextColor);
    }
}
