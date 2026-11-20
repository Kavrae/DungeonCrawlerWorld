using Engine.Math;
using Game.Blueprints;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Characteristics;

/// <summary>An entity's footprint drawn as one filled square per tile, on the scale of the largest footprint, on the icon's bottom edge and centred across.</summary>
/// <remarks>Every footprint shares one cell size -- the icon's width over the largest footprint's -- so a 1x1 reads smaller than a 2x2 on its own, and a 1x2 is a column of two.</remarks>
public sealed class FootprintIconElement(ElementServices services) : Element(services)
{
    /// <summary>The gap between neighbouring cells, as a fraction of a cell.</summary>
    public const float CellGapFraction = 0.15f;

    private Vector2Byte _footprint = new(1, 1);
    private Color _color;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        CanUserFocus = false;
    }

    public void Configure(Vector2Byte footprint, Color color)
    {
        _footprint = footprint;
        _color = color;
    }

    public override void DrawContent(GameTime gameTime)
    {
        for (var row = 0; row < _footprint.Y; row++)
        {
            for (var column = 0; column < _footprint.X; column++)
            {
                Render.Batch.FillRectangle(CellRectangle(ContentSize, _footprint, column, row), _color);
            }
        }
    }

    /// <summary>Where the tile at column and row of footprint draws in an icon iconSize across, in local pixels.</summary>
    public static Rectangle CellRectangle(Vector2 iconSize, Vector2Byte footprint, int column, int row)
    {
        var tilesAcross = System.Math.Max((int)ResolvedBlueprint.MaximumFootprintTiles, (int)System.Math.Max(footprint.X, footprint.Y));
        var cellSize = System.Math.Min(iconSize.X, iconSize.Y) / tilesAcross;
        var gap = System.Math.Max(1f, cellSize * CellGapFraction);
        var left = (iconSize.X - footprint.X * cellSize) / 2f;
        var top = iconSize.Y - footprint.Y * cellSize;

        return PixelRectangle.Snapped(
            new Vector2(left + column * cellSize + gap / 2f, top + row * cellSize + gap / 2f),
            new Vector2(cellSize - gap, cellSize - gap));
    }
}
