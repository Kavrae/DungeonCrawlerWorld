using Engine.Math;
using Microsoft.Xna.Framework;
using SpriteFormat;

namespace UIEngine.Rendering;

/// <summary>Where a placed sprite draws on the map, in pixels: SpritePlacementTiles' area in tiles, scaled to where its footprint is drawn.</summary>
/// <remarks>
/// The rules -- sized by its pixels per tile, scaled with its footprint, its pivot on the footprint's bottom-centre, shrunk to stay in the
/// reach box -- are the sprite format's (SpritePlacementTiles), shared with DevTools/SpriteManifestBuilder's preview.
/// </remarks>
public static class SpritePlacementMath
{
    /// <summary>Where the sprite draws, in tiles from the footprint's top-left corner.</summary>
    public static SpriteTileArea DrawnTiles(Vector2Byte footprint, Point sourceSize, SpritePlacement? placement) =>
        SpritePlacementTiles.DrawnTiles(footprint.X, footprint.Y, sourceSize.X, sourceSize.Y, placement);

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
            new Vector2(area.Width * tileWidth, area.Height * tileHeight));
    }

    /// <summary>Where the health bar and the charging badge anchor: the top of the drawn sprite, so a tall sprite's head doesn't cover them, but never lower than the footprint's top.</summary>
    public static float CueTop(float drawnTop, float footprintTop) => System.Math.Min(drawnTop, footprintTop);
}
