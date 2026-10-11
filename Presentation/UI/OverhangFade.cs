using Engine.Math;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>Which live wall pieces fade so whoever is behind them stays visible: any overhang drawn over a watched entity's drawn sprite.</summary>
/// <remarks>
/// <para>
/// Watched: the player always, and the inspected entity while inspection is Detail or Admin -- at most two rectangles, so no
/// spatial index. Each is the entity's drawn sprite (SpritePlacementMath), which for a 1x1 stretched sprite is exactly its
/// tile and for a placed one covers a tall head or a wide sprite reaching into the next column.
/// </para>
/// <para>
/// Only overhangs fade: a cached piece covers only walls, where nobody stands, and a Phasing entity inside a wall already
/// draws over it. The fade is instant rather than eased in, since easing needs per-piece state for a cosmetic nicety.
/// Rectangles that only touch (a piece on the tile beside the player) don't count.
/// </para>
/// </remarks>
public static class OverhangFade
{
    public const float FadedAlpha = 0.7f;

    /// <summary>Fills watchedEntityIds with the player and, in Detail or Admin inspection, the inspected entity (once, if they are the same).</summary>
    public static void CollectWatchedEntities(List<int> watchedEntityIds, int playerEntityId, InspectionMode inspectionMode, int inspectedEntityId)
    {
        watchedEntityIds.Clear();
        if (playerEntityId >= 0)
        {
            watchedEntityIds.Add(playerEntityId);
        }

        if (inspectionMode is InspectionMode.Detail or InspectionMode.Admin && inspectedEntityId >= 0 && inspectedEntityId != playerEntityId)
        {
            watchedEntityIds.Add(inspectedEntityId);
        }
    }

    /// <summary>Where a watched entity's picture is drawn: its sprite by its placement, or its footprint for a glyph or a sprite with none.</summary>
    public static Rectangle WatchedRectangle(Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, SpriteView? sprite)
    {
        var (topLeft, size) = sprite is { } spriteView
            ? SpritePlacementMath.DrawnRectangle(footprintTopLeft, footprintSize, footprintTiles, new Point(spriteView.SourceRectangle.Width, spriteView.SourceRectangle.Height), spriteView.Placement)
            : (footprintTopLeft, footprintSize);

        return new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)size.X, (int)size.Y);
    }

    /// <summary>FadedAlpha when the overhang's drawn rectangle overlaps any watched rectangle, otherwise fully opaque.</summary>
    public static float AlphaFor(Rectangle overhang, IReadOnlyList<Rectangle> watched)
    {
        foreach (var rectangle in watched)
        {
            if (overhang.Intersects(rectangle))
            {
                return FadedAlpha;
            }
        }

        return 1f;
    }
}
