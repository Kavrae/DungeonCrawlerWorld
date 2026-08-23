using FontStashSharp;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI;

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
public sealed class AbilityScoreModifierRow(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    private const float FontFraction = 0.6f;

    private const float Padding = 3f;

    private static readonly Color TextColor = WindowPalette.BodyTextColor;

    private string _text = string.Empty;
    private SpriteFontBase _font = null!;

    public StatusEffectSource? Source { get; private set; }

    public string? ModifierText { get; private set; }

    public ushort? RemainingDurationFrames { get; private set; }

    public bool IsHovered { get; set; }

    public void Configure(ModifierDisplayLine line, float rowHeight)
    {
        _text = line.Text;
        Source = line.Source;
        ModifierText = line.ModifierText;
        RemainingDurationFrames = line.RemainingDurationFrames;
        _font = fontService.GetFont((int)(rowHeight * FontFraction));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y), WindowPalette.HighlightColor);
        }

        var footprintSize = new Vector2(ContentSize.X - Padding, ContentSize.Y);
        LabelRenderer.DrawRightAligned(spriteBatch, _font, _text, ContentAbsolutePosition, footprintSize, TextColor);
    }
}
