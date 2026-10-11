using Engine.Math;
using Game.Sprites;
using Microsoft.Xna.Framework;

namespace Presentation.Rendering;

/// <summary>Where a placed sprite draws on the map: sized by its pixels per tile, scaled with its footprint, its pivot on the footprint's bottom-centre, and kept inside the reach box.</summary>
/// <remarks>
/// Placement is authored for a 1x1 footprint (manifest load checks it fits the reach box there), and a larger footprint draws
/// the same art scaled per axis -- what stretching already does -- so a 2x2 or Huge goblin is the same picture, bigger, and a
/// 1x2 ghost keeps its shape. Scaling up can carry a sprite out of the reach box, which the map's row walk overscans by exactly,
/// so it is then shrunk uniformly about its pivot until it fits: never clipped, never drawn where the walk can't reach. A sprite
/// with no placement fills its footprint. DevTools/SpriteManifestBuilder's preview mirrors this; keep the two in sync.
/// </remarks>
public static class SpritePlacementMath
{
    /// <summary>An area in tiles, relative to the footprint's top-left corner, y down.</summary>
    public readonly record struct TileArea(float Left, float Top, float Right, float Bottom);

    /// <summary>Where the sprite draws, in tiles from the footprint's top-left corner.</summary>
    public static TileArea DrawnTiles(Vector2Byte footprint, Point sourceSize, SpritePlacement? placement)
    {
        if (placement is not { } placed)
        {
            return new TileArea(0, 0, footprint.X, footprint.Y);
        }

        var pivotX = footprint.X / 2f;
        var pivotY = (float)footprint.Y;
        var scaleX = footprint.X / (float)placed.PixelsPerTile;
        var scaleY = footprint.Y / (float)placed.PixelsPerTile;

        var extentLeft = placed.PivotX * scaleX;
        var extentRight = (sourceSize.X - placed.PivotX) * scaleX;
        var extentAbove = placed.PivotY * scaleY;
        var extentBelow = (sourceSize.Y - placed.PivotY) * scaleY;

        var reachLeft = -SpriteReach.MaximumColumnsBeside;
        var reachRight = 1 + SpriteReach.MaximumColumnsBeside;
        var reachTop = footprint.Y - 1 - SpriteReach.MaximumRowsAbove;

        var shrink = 1f;
        shrink = Fit(shrink, extentLeft, pivotX - reachLeft);
        shrink = Fit(shrink, extentRight, reachRight - pivotX);
        shrink = Fit(shrink, extentAbove, pivotY - reachTop);
        shrink = Fit(shrink, extentBelow, 0);

        return new TileArea(pivotX - extentLeft * shrink, pivotY - extentAbove * shrink, pivotX + extentRight * shrink, pivotY + extentBelow * shrink);
    }

    /// <summary>Where the sprite draws in pixels, given where its footprint is drawn.</summary>
    public static (Vector2 TopLeft, Vector2 Size) DrawnRectangle(Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprint, Point sourceSize, SpritePlacement? placement)
    {
        if (placement is null)
        {
            return (footprintTopLeft, footprintSize);
        }

        var tileWidth = footprintSize.X / footprint.X;
        var tileHeight = footprintSize.Y / footprint.Y;
        var area = DrawnTiles(footprint, sourceSize, placement);

        return (
            new Vector2(footprintTopLeft.X + area.Left * tileWidth, footprintTopLeft.Y + area.Top * tileHeight),
            new Vector2((area.Right - area.Left) * tileWidth, (area.Bottom - area.Top) * tileHeight));
    }

    /// <summary>Where the health bar and the charging badge anchor: the top of the drawn sprite, so a tall sprite's head doesn't cover them, but never lower than the footprint's top.</summary>
    public static float CueTop(float drawnTop, float footprintTop) => System.Math.Min(drawnTop, footprintTop);

    private static float Fit(float shrink, float extent, float room) =>
        extent > 0 && extent > room ? System.Math.Min(shrink, System.Math.Max(0, room) / extent) : shrink;
}
