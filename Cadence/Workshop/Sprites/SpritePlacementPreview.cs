using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFormat;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Sprites;

/// <summary>An entry's piece as the map places it: on a grid of tiles around its anchor tile, the footprint shaded, the reach box outlined, its pivot marked -- and, for a wall, its top raised onto its face.</summary>
/// <remarks>
/// Drawn through the sprite effect (SpriteEffectBatch), so a tint, a turn or a flip looks exactly as on the map. Placement is
/// SpritePlacementTiles' -- the game's -- at the chosen footprint; a piece breaking out of the reach box is outlined in red,
/// which only the 1x1 footprint can show (a larger one shrinks the sprite to fit, as the game does).
/// </remarks>
public sealed class SpritePlacementPreview(ElementServices services)
    : Element(services)
{
    private const int TilesLeftOfAnchor = 3;
    private const int TilesRightOfAnchor = 3;
    private const int TilesAboveAnchor = 3;
    private const int TilesBelowAnchor = 1;
    private const int ColumnCount = TilesLeftOfAnchor + 1 + TilesRightOfAnchor;
    private const int RowCount = TilesAboveAnchor + 1 + TilesBelowAnchor;
    private const int CrosshairHalfLength = 6;

    private static readonly Color BackgroundColor = new(30, 30, 30);
    private static readonly Color GridColor = new(70, 70, 70);
    private static readonly Color FootprintColor = new Color(70, 130, 255) * 0.35f;
    private static readonly Color ReachBoxColor = Color.OrangeRed;
    private static readonly Color BreachColor = Color.Red;
    private static readonly Color PivotColor = Color.Cyan;

    private SpriteCellDrawer _drawer = null!;
    private SpriteEntryView? _entry;

    /// <summary>The footprint, in tiles a side, the piece is placed on: 1 to 3.</summary>
    public int FootprintTiles { get; set; } = 1;

    protected override bool RequiresContentViewport => true;

    public void Configure(SpriteCellDrawer drawer) => _drawer = drawer;

    public void Show(SpriteEntryView? entry) => _entry = entry;

    public override void DrawContent(GameTime gameTime)
    {
        var tilePixels = Math.Max(1, (int)Math.Min(ContentSize.X / ColumnCount, ContentSize.Y / RowCount));
        DrawPreview(Render.Batch, tilePixels);
    }

    private void DrawPreview(SpriteEffectBatch batch, int tilePixels)
    {
        batch.FillRectangle(new Rectangle(0, 0, (int)ContentSize.X, (int)ContentSize.Y), BackgroundColor, SpriteDrawStyle.Plain);
        batch.FillRectangle(ToPixels(new SpriteTileArea(0, 0, FootprintTiles, FootprintTiles), tilePixels), FootprintColor, SpriteDrawStyle.Plain);
        for (var column = 0; column <= ColumnCount; column++)
        {
            batch.FillRectangle(new Rectangle(column * tilePixels, 0, 1, RowCount * tilePixels), GridColor, SpriteDrawStyle.Plain);
        }

        for (var row = 0; row <= RowCount; row++)
        {
            batch.FillRectangle(new Rectangle(0, row * tilePixels, ColumnCount * tilePixels, 1), GridColor, SpriteDrawStyle.Plain);
        }

        var reachBox = ToPixels(SpritePlacementTiles.ReachBox(FootprintTiles), tilePixels);
        if (_entry is { } entry)
        {
            foreach (var part in entry.Parts)
            {
                DrawPart(batch, part, tilePixels, reachBox);
            }
        }

        SpriteCellDrawer.DrawOutline(batch, reachBox, ReachBoxColor, 2);
    }

    private void DrawPart(SpriteEffectBatch batch, SpriteEntryPartView part, int tilePixels, Rectangle reachBox)
    {
        if (part.PreviewCell is not { } cell)
        {
            return;
        }

        var placement = part.Part.PlacementFor(cell);
        var drawn = ToPixels(SpritePlacementTiles.DrawnTiles(FootprintTiles, FootprintTiles, cell.SourceWidth, cell.SourceHeight, placement, part.RaisedRows), tilePixels);
        _drawer.Draw(batch, cell, new Vector2(drawn.X, drawn.Y), new Vector2(drawn.Width, drawn.Height));

        if (!reachBox.Contains(drawn))
        {
            SpriteCellDrawer.DrawOutline(batch, drawn, BreachColor, 2);
        }

        if (placement is not null)
        {
            var pivot = ToPixels(new SpriteTileArea(FootprintTiles / 2f, FootprintTiles - part.RaisedRows, FootprintTiles / 2f, FootprintTiles - part.RaisedRows), tilePixels);
            batch.FillRectangle(new Rectangle(pivot.X - CrosshairHalfLength, pivot.Y, CrosshairHalfLength * 2 + 1, 1), PivotColor, SpriteDrawStyle.Plain);
            batch.FillRectangle(new Rectangle(pivot.X, pivot.Y - CrosshairHalfLength, 1, CrosshairHalfLength * 2 + 1), PivotColor, SpriteDrawStyle.Plain);
        }
    }

    /// <summary>area, in tiles from the footprint's top-left corner (SpritePlacementTiles' frame), as pixels in this view: the anchor tile -- the footprint's bottom-left -- sits TilesLeftOfAnchor and TilesAboveAnchor tiles in.</summary>
    private Rectangle ToPixels(SpriteTileArea area, int tilePixels)
    {
        var top = area.Top + 1 - FootprintTiles + TilesAboveAnchor;
        var left = area.Left + TilesLeftOfAnchor;
        return new Rectangle(
            (int)MathF.Round(left * tilePixels),
            (int)MathF.Round(top * tilePixels),
            (int)MathF.Round(area.Width * tilePixels),
            (int)MathF.Round(area.Height * tilePixels));
    }
}
