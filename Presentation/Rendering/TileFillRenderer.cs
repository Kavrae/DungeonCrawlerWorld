using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Bottom-up percentage fill of a single tile rectangle -- the map-tile counterpart to
/// ResourceBarRenderer's horizontal fill and RadialFillRenderer's radial sweep, both of which
/// live alongside this in the same "draw N% of a shape" primitive family (distinct from
/// GlowRenderer's ring-fade family, see PLAN-charge-attack-fill-indicator.md's own naming
/// investigation).
/// </summary>
public static class TileFillRenderer
{
    /// <summary>No inset, unlike DrawHealthBar's 1px outline inset -- at fillFraction 1, this exactly covers tileRectangle, matching the full-tile wash callers already draw underneath it.</summary>
    public static void DrawBottomUpFill(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle tileRectangle, float fillFraction, Color color)
    {
        var clampedFraction = Math.Clamp(fillFraction, 0f, 1f);
        if (clampedFraction <= 0f)
        {
            return;
        }

        var fillHeight = (int)(tileRectangle.Height * clampedFraction);
        if (fillHeight <= 0)
        {
            return;
        }

        var fillRectangle = new Rectangle(tileRectangle.X, tileRectangle.Bottom - fillHeight, tileRectangle.Width, fillHeight);
        spriteBatch.Draw(unitRectangle, fillRectangle, color);
    }
}
