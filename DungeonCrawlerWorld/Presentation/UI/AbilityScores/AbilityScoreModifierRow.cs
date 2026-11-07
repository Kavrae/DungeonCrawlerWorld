using Engine.Utilities;
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
/// AbilityScoreWindow's hover popup reads. A modifier row highlights while
/// IsPointerOver, as AbilityScoreColumnHeader does; the Base line never does.
/// </summary>
public sealed class AbilityScoreModifierRow(ElementServices services)
    : Element(services)
{
    /// <summary>Clears the column's own InteriorFade glow (see AbilityScoreWindow.BuildColumns' SetOverlayGlow on the list window this row tiles inside) -- GlowRenderer's 5 rings reach 5px deep from the list window's own right edge, which this row's right edge sits flush against, so right-aligned text needs at least that much clearance plus a little breathing room to avoid sitting under the glow.</summary>
    private const float Padding = 8f;

    private static readonly Color TextColor = WindowPalette.TitleTextColor;

    private string _text = string.Empty;
    private string? _sourceName;
    private SpriteFontBase _font = null!;
    private readonly TextSizeCache _textSize = new();

    /// <summary>The line as drawn, fitted to the row's width -- rebuilt only when the line, the width or the font changes.</summary>
    private readonly KeyedCache<(string Text, string? SourceName, string? ModifierText, float Width, SpriteFontBase Font), string> _drawnText = new();

    public ActionSource? Source { get; private set; }

    public string? ModifierText { get; private set; }

    public int? RemainingDurationFrames { get; private set; }

    public void Configure(ModifierDisplayLine line, float rowHeight)
    {
        _text = line.Text;
        _sourceName = line.SourceName;
        Source = line.Source;
        ModifierText = line.ModifierText;
        RemainingDurationFrames = line.RemainingDurationFrames;
        _font = FontService.GetFont((int)(rowHeight * GameFontChrome.AbilityScoreModifierRowFontFraction));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (IsPointerOver && Source is not null)
        {
            spriteBatch.Draw(unitRectangle, LocalContentRectangle, WindowPalette.HighlightColor);
        }

        var footprintSize = new Vector2(ContentSize.X - Padding, ContentSize.Y);
        var drawnText = _drawnText.Get((_text, _sourceName, ModifierText, footprintSize.X, _font), FontService, static (key, fontService) => FitToWidth(fontService.GetTextMeasurer(key.Font), key.Text, key.SourceName, key.ModifierText, key.Width));
        LabelRenderer.DrawRightAligned(spriteBatch, _font, drawnText, _textSize.SizeOf(_font, drawnText).X, Vector2.Zero, footprintSize, TextColor);
    }

    /// <summary>The line unchanged when it fits width, else with the source name shortened by an ellipsis so the value after it still shows; a line with no source is shortened as a whole.</summary>
    internal static string FitToWidth(ITextMeasurer textMeasurer, string text, string? sourceName, string? modifierText, float width)
    {
        if (textMeasurer.MeasureWidth(text) <= width)
        {
            return text;
        }

        if (sourceName is null || modifierText is null)
        {
            return StringUtility.TruncateWithEllipsis(textMeasurer, text, width);
        }

        var valueSuffix = $" : {modifierText}";
        return StringUtility.TruncateWithEllipsis(textMeasurer, sourceName, width - textMeasurer.MeasureWidth(valueSuffix)) + valueSuffix;
    }
}
