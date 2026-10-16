using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>
/// Directional percentage fill of a single tile rectangle -- the map-tile counterpart to
/// ResourceBarRenderer's horizontal fill and RadialFillRenderer's radial sweep, both of which
/// live alongside this in the same "draw N% of a shape" primitive family (distinct from
/// GlowRenderer's ring-fade family).
/// </summary>
public static class TileFillRenderer
{
    /// <summary>No inset, unlike DrawHealthBar's 1px outline inset -- at fillFraction 1, this exactly covers tileRectangle, matching the full-tile wash callers already draw underneath it.</summary>
    public static void DrawFill(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle tileRectangle, float fillFraction, TileFillDirection fillDirection, Color color)
    {
        var clampedFraction = Math.Clamp(fillFraction, 0f, 1f);
        if (clampedFraction <= 0f)
        {
            return;
        }

        var fillRectangle = FillRectangleFor(tileRectangle, clampedFraction, fillDirection);
        if (fillRectangle.Width <= 0 || fillRectangle.Height <= 0)
        {
            return;
        }

        spriteBatch.Draw(unitRectangle, fillRectangle, color);
    }

    private static Rectangle FillRectangleFor(Rectangle tileRectangle, float clampedFraction, TileFillDirection fillDirection)
    {
        var fillHeight = (int)(tileRectangle.Height * clampedFraction);
        var fillWidth = (int)(tileRectangle.Width * clampedFraction);
        return fillDirection switch
        {
            TileFillDirection.BottomToTop => new Rectangle(tileRectangle.X, tileRectangle.Bottom - fillHeight, tileRectangle.Width, fillHeight),
            TileFillDirection.TopToBottom => new Rectangle(tileRectangle.X, tileRectangle.Y, tileRectangle.Width, fillHeight),
            TileFillDirection.LeftToRight => new Rectangle(tileRectangle.X, tileRectangle.Y, fillWidth, tileRectangle.Height),
            TileFillDirection.RightToLeft => new Rectangle(tileRectangle.Right - fillWidth, tileRectangle.Y, fillWidth, tileRectangle.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(fillDirection), fillDirection, null)
        };
    }
}
