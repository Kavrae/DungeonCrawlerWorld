using FontStashSharp;
using Game.Modules.AbilityScores;
using Microsoft.Xna.Framework;

using Presentation.UI.Chrome;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// The non-scrolling header at the top of one ability-score column: the score's name centered
/// on one line, its Total centered on the line below. A plain Element (not Window) -- same
/// draw-only reasoning as InventoryItemStackCell: no title/chrome needed, just draws itself.
/// IsHovered drives a translucent highlight overlay (see AbilityScoreWindow's own hover
/// polling) -- immediate, unlike the hover-popup delay, since a highlight is instant feedback.
/// </summary>
public sealed class AbilityScoreColumnHeader(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private static readonly Color TextColor = WindowPalette.TitleTextColor;

    private string _name = string.Empty;
    private string _totalText = string.Empty;
    private FontStashTextMeasurer _nameFontMeasurer = null!;
    private FontStashTextMeasurer _totalFontMeasurer = null!;

    public AbilityScoreType Type { get; private set; }

    public bool IsHovered { get; set; }

    public void Configure(AbilityScoreType type, ushort total, Vector2 headerSize)
    {
        Type = type;
        _name = type.ToString();
        _totalText = total.ToString();
        _nameFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(headerSize.Y * GameFontChrome.AbilityScoreColumnNameFontFraction)));
        _totalFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(headerSize.Y * GameFontChrome.AbilityScoreColumnTotalFontFraction)));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, LocalContentRectangle, WindowPalette.HighlightColor);
        }

        var stripSize = new Vector2(ContentSize.X, ContentSize.Y / 2f);

        LabelRenderer.DrawCentered(spriteBatch, _nameFontMeasurer, _name, Vector2.Zero, stripSize, TextColor);
        LabelRenderer.DrawCentered(spriteBatch, _totalFontMeasurer, _totalText, new Vector2(0, stripSize.Y), stripSize, TextColor);
    }
}
