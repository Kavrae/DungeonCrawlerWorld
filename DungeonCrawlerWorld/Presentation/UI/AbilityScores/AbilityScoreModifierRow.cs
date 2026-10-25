using FontStashSharp;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// One line in an ability-score column's scrolling list ("Base : 6", "Some Source : +2",
/// "Another Source : -10%") -- right-aligned within its own bounds so the trailing numbers/
/// percentages line up regardless of source-name length. A plain Element (not Window), same
/// draw-only style as InventoryItemStackCell/AbilityScoreColumnHeader. Source/ModifierText/
/// RemainingDurationFrames (null for the non-modifier "Base : N" line) are what
/// AbilityScoreWindow's hover popup reads. IsHovered mirrors AbilityScoreColumnHeader's own
/// immediate highlight-on-hover.
/// </summary>
public sealed class AbilityScoreModifierRow(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    /// <summary>Clears the column's own InteriorFade glow (see AbilityScoreWindow.BuildColumns' SetOverlayGlow on the list window this row tiles inside) -- GlowRenderer's 5 rings reach 5px deep from the list window's own right edge, which this row's right edge sits flush against, so right-aligned text needs at least that much clearance plus a little breathing room to avoid sitting under the glow.</summary>
    private const float Padding = 8f;

    private static readonly Color TextColor = WindowPalette.TitleTextColor;

    private string _text = string.Empty;
    private SpriteFontBase _font = null!;
    private readonly TextSizeCache _textSize = new();

    public ActionSource? Source { get; private set; }

    public string? ModifierText { get; private set; }

    public int? RemainingDurationFrames { get; private set; }

    public bool IsHovered { get; set; }

    public void Configure(ModifierDisplayLine line, float rowHeight)
    {
        _text = line.Text;
        Source = line.Source;
        ModifierText = line.ModifierText;
        RemainingDurationFrames = line.RemainingDurationFrames;
        _font = FontService.GetFont((int)(rowHeight * GameFontChrome.AbilityScoreModifierRowFontFraction));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, LocalContentRectangle, WindowPalette.HighlightColor);
        }

        var footprintSize = new Vector2(ContentSize.X - Padding, ContentSize.Y);
        LabelRenderer.DrawRightAligned(spriteBatch, _font, _text, _textSize.SizeOf(_font, _text).X, Vector2.Zero, footprintSize, TextColor);
    }
}
