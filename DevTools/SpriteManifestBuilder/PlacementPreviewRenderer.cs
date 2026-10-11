using System.Drawing.Drawing2D;

namespace SpriteManifestBuilder;

/// <summary>A sprite to draw in the placement preview: a cell and the placement it draws by.</summary>
public sealed record PlacedCell(ManifestCell Cell, int? PixelsPerTile, Point Pivot, int RaisedRows);

/// <summary>
/// Draws one option as the map will: on a grid of map tiles around its anchor tile, the footprint shaded, the reach box
/// outlined, a crosshair on the pivot, the sprite sized and clamped by SpritePlacementRules (outlined in red where it
/// breaks out of the reach box, which only a 1x1 preview can show). A wall's top is drawn raised
/// onto its face, with the face drawn faintly beneath it.
/// </summary>
public static class PlacementPreviewRenderer
{
    private const int TilesLeftOfAnchor = 3;
    private const int TilesRightOfAnchor = 3;
    private const int TilesAboveAnchor = 3;
    private const int TilesBelowAnchor = 1;
    private const int ColumnCount = TilesLeftOfAnchor + 1 + TilesRightOfAnchor;
    private const int RowCount = TilesAboveAnchor + 1 + TilesBelowAnchor;
    private const float BeneathOpacity = 0.4f;

    /// <param name="beneath">The wall's face, drawn faintly under a top being edited; null otherwise.</param>
    public static void Paint(Graphics graphics, Size canvasSize, PlacedCell? placed, PlacedCell? beneath, int footprintTiles, SheetImageCache sheets)
    {
        var tilePixels = TilePixels(canvasSize);

        using var backgroundBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        graphics.FillRectangle(backgroundBrush, 0, 0, canvasSize.Width, canvasSize.Height);

        using var footprintBrush = new SolidBrush(Color.FromArgb(90, 70, 130, 255));
        graphics.FillRectangle(footprintBrush, ToPixels(SpritePlacementRules.Footprint(footprintTiles), tilePixels));

        using var gridPen = new Pen(Color.FromArgb(70, 70, 70));
        for (var column = 0; column <= ColumnCount; column++)
        {
            graphics.DrawLine(gridPen, column * tilePixels, 0, column * tilePixels, RowCount * tilePixels);
        }
        for (var row = 0; row <= RowCount; row++)
        {
            graphics.DrawLine(gridPen, 0, row * tilePixels, ColumnCount * tilePixels, row * tilePixels);
        }

        if (beneath is not null)
        {
            CellDrawing.Draw(graphics, sheets, beneath.Cell, DrawnRectangle(canvasSize, beneath, footprintTiles), BeneathOpacity);
        }

        var reachBox = ToPixels(SpritePlacementRules.ReachBox, tilePixels);
        if (placed is not null)
        {
            var drawn = DrawnRectangle(canvasSize, placed, footprintTiles);
            CellDrawing.Draw(graphics, sheets, placed.Cell, drawn);

            if (!Contains(reachBox, drawn))
            {
                using var breachPen = new Pen(Color.Red, 2);
                graphics.DrawRectangle(breachPen, drawn.X, drawn.Y, drawn.Width, drawn.Height);
            }
        }

        using var reachBoxPen = new Pen(Color.OrangeRed, 2) { DashStyle = DashStyle.Dash };
        graphics.DrawRectangle(reachBoxPen, reachBox.X, reachBox.Y, reachBox.Width, reachBox.Height);

        if (placed is { PixelsPerTile: not null })
        {
            var footprint = SpritePlacementRules.Footprint(footprintTiles);
            var pivotPoint = ToPixels(new PointF(footprint.Left + footprintTiles / 2f, footprint.Bottom - placed.RaisedRows), tilePixels);
            using var crosshairPen = new Pen(Color.Cyan, 1);
            graphics.DrawLine(crosshairPen, pivotPoint.X - 8, pivotPoint.Y, pivotPoint.X + 8, pivotPoint.Y);
            graphics.DrawLine(crosshairPen, pivotPoint.X, pivotPoint.Y - 8, pivotPoint.X, pivotPoint.Y + 8);
        }
    }

    /// <summary>Where the cell's pixels land on the canvas, for drawing it and for turning a click on it back into a source pixel.</summary>
    public static RectangleF DrawnRectangle(Size canvasSize, PlacedCell placed, int footprintTiles) =>
        ToPixels(
            SpritePlacementRules.DrawnTiles(placed.Cell.SourceRectangle.Size, placed.PixelsPerTile, placed.Pivot, footprintTiles, placed.RaisedRows),
            TilePixels(canvasSize));

    /// <summary>Whether inner lies inside outer, give or take rounding.</summary>
    private static bool Contains(RectangleF outer, RectangleF inner)
    {
        const float tolerance = 0.5f;
        return inner.Left >= outer.Left - tolerance && inner.Top >= outer.Top - tolerance && inner.Right <= outer.Right + tolerance && inner.Bottom <= outer.Bottom + tolerance;
    }

    private static int TilePixels(Size canvasSize) => Math.Max(1, Math.Min(canvasSize.Width / ColumnCount, canvasSize.Height / RowCount));

    private static RectangleF ToPixels(RectangleF tiles, int tilePixels) =>
        RectangleF.FromLTRB(
            (tiles.Left + TilesLeftOfAnchor) * tilePixels,
            (tiles.Top + TilesAboveAnchor) * tilePixels,
            (tiles.Right + TilesLeftOfAnchor) * tilePixels,
            (tiles.Bottom + TilesAboveAnchor) * tilePixels);

    private static PointF ToPixels(PointF tiles, int tilePixels) =>
        new((tiles.X + TilesLeftOfAnchor) * tilePixels, (tiles.Y + TilesAboveAnchor) * tilePixels);
}
