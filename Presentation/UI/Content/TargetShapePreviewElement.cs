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
/// </summary>
public sealed class TargetShapePreviewElement(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float CellGap = 1f;
    private const float CellBorderThickness = 1f;
    private const float PlayerMarkerInset = 3f;
    private const int CircleSliverCount = 48;

    private static readonly Color CellBorderColor = WindowPalette.BorderColor;
    private static readonly Color CellFillColor = WindowPalette.PanelContentColor;
    private static readonly Color PlayerMarkerColor = Color.Gold;

    private IReadOnlyList<Point> _offsets = [];
    private int _minX;
    private int _minY;
    private float _cellSize;

    public void Configure(IReadOnlyList<Point> offsets, int minX, int minY, float cellSize)
    {
        _offsets = offsets;
        _minX = minX;
        _minY = minY;
        _cellSize = cellSize;
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

        spriteBatch.Draw(unitRectangle, new Rectangle((int)cellOrigin.X, (int)cellOrigin.Y, (int)outerSize, (int)outerSize), CellBorderColor);

        var innerSize = outerSize - CellBorderThickness * 2;
        if (innerSize > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle((int)(cellOrigin.X + CellBorderThickness), (int)(cellOrigin.Y + CellBorderThickness), (int)innerSize, (int)innerSize), CellFillColor);
        }
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
