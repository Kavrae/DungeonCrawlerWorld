using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// A small, static grid rendering of a TargetingSpec's own shape -- one bordered square per tile
/// TargetShapePreviewGeometry.ComputeOffsets resolved, plus a filled circle marking the caster's
/// own cell, replacing ItemDetailsWindow's former plain "Targeting: Shape, Range N" text line.
/// Sized entirely up front by the caller (ItemDetailsWindow.BuildActivationSection computes
/// columns/rows/cellSize from the same TargetingSpec before creating this Element and passes them
/// straight into ElementOptions.Layout.Size) -- no dynamic remeasurement here at all, avoiding the
/// Fixed-vs-WrapContent sizing pitfall the details window itself hit earlier.
///
/// Cells to the right of the caster on its own row (offset.Y == 0, offset.X > 0) also get a
/// distance number -- offset.X, the same "area size 3 means 3 tiles out" reading
/// RangeIndicatorElement's own ruler gives the Range stat -- so a Burst's AreaSize isn't just an
/// abstract stat either. The caster's own cell and the mirrored left side are left blank -- the
/// left side would just repeat the same distance values already visible on the right.
/// </summary>
public sealed class TargetShapePreviewElement(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float CellGap = 1f;
    private const float CellBorderThickness = 1f;
    private const float PlayerMarkerInset = 3f;
    private const int CircleSliverCount = 48;
    private const float NumberFontFraction = 0.6f;
    private const int MinNumberFontSize = 6;

    private static readonly Color CellBorderColor = WindowPalette.BorderColor;
    private static readonly Color CellFillColor = WindowPalette.PanelContentColor;
    private static readonly Color PlayerMarkerColor = Color.Gold;
    private static readonly Color DistanceNumberColor = WindowPalette.BorderColor;

    /// <summary>Item Details Comparison's own tile-level diff highlight -- matches ItemDetailsWindow.BetterColor, so "green" reads as "advantage" consistently across every part of this feature.</summary>
    private static readonly Color HighlightedCellFillColor = Color.LightGreen;

    private IReadOnlyList<Point> _offsets = [];
    private int _minX;
    private int _minY;
    private float _cellSize;
    private IReadOnlySet<Point>? _highlightedOffsets;
    private SpriteFontBase _numberFont = null!;

    /// <summary>highlightedOffsets is null outside comparison (every cell plain) -- see ItemDetailsWindow's own shape-match-gated diff computation for when/how it's populated.</summary>
    public void Configure(IReadOnlyList<Point> offsets, int minX, int minY, float cellSize, IReadOnlySet<Point>? highlightedOffsets = null)
    {
        _offsets = offsets;
        _minX = minX;
        _minY = minY;
        _cellSize = cellSize;
        _highlightedOffsets = highlightedOffsets;
        _numberFont = FontService.GetFont(System.Math.Max(MinNumberFontSize, (int)(cellSize * NumberFontFraction)));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        foreach (var offset in _offsets)
        {
            DrawCell(spriteBatch, unitRectangle, offset);
        }

        DrawPlayerMarker(spriteBatch, unitRectangle);
    }

    private void DrawCell(SpriteBatch spriteBatch, Texture2D unitRectangle, Point offset)
    {
        var cellOrigin = ContentAbsolutePosition + new Vector2((offset.X - _minX) * _cellSize, (offset.Y - _minY) * _cellSize);
        var outerSize = _cellSize - CellGap;

        var fillColor = _highlightedOffsets?.Contains(offset) == true ? HighlightedCellFillColor : CellFillColor;
        BorderedBoxRenderer.Draw(spriteBatch, unitRectangle, cellOrigin, outerSize, CellBorderThickness, CellBorderColor, fillColor);

        DrawDistanceNumber(spriteBatch, offset, cellOrigin, outerSize);
    }

    /// <summary>Only the caster's own row (offset.Y == 0) and only to the right of the caster (offset.X > 0) -- the left side would show the same distance values again, redundant once the right side already reads them.</summary>
    private void DrawDistanceNumber(SpriteBatch spriteBatch, Point offset, Vector2 cellOrigin, float outerSize)
    {
        if (offset.Y != 0 || offset.X <= 0)
        {
            return;
        }

        var text = offset.X.ToString();
        GlyphRenderer.DrawCentered(spriteBatch, _numberFont, text, cellOrigin, new Vector2(outerSize, outerSize), DistanceNumberColor);
    }

    /// <summary>The caster's own cell (0,0) -- always present, whether or not it's also one of the targeted _offsets (Self/AdjacentWithSelf target it, most other shapes don't; either way the circle marks it).</summary>
    private void DrawPlayerMarker(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var cellOrigin = ContentAbsolutePosition + new Vector2((0 - _minX) * _cellSize, (0 - _minY) * _cellSize);
        var center = cellOrigin + new Vector2(_cellSize / 2f, _cellSize / 2f);
        var radius = _cellSize / 2f - PlayerMarkerInset;
        if (radius <= 0)
        {
            return;
        }

        DrawCircle(spriteBatch, unitRectangle, center, radius, PlayerMarkerColor);
    }

    /// <summary>Same "sweep thin rotated rectangles around a center" technique RadialFillRenderer.DrawRadialMask already uses for its own cooldown wheel -- reimplemented directly rather than reused, since that method draws a translucent cooldown mask over an icon (a different composite), not a plain solid circle in an arbitrary color.</summary>
    private static void DrawCircle(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 center, float radius, Color color)
    {
        var sliverThickness = MathF.Max(1f, MathF.Tau * radius / CircleSliverCount * 1.5f);
        var sliverSize = new Vector2(radius, sliverThickness);
        var angleStep = MathHelper.TwoPi / CircleSliverCount;
        var angle = 0f;

        for (var i = 0; i < CircleSliverCount; i++)
        {
            spriteBatch.Draw(unitRectangle, center, null, color, angle, new Vector2(0f, 0.5f), sliverSize, SpriteEffects.None, 0f);
            angle -= angleStep;
        }
    }
}
