using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

/// <summary>
/// Draws a soft outward glow around a rectangle -- shared by any Window that opts in via
/// SetGlow. No shader/gradient support exists in this renderer stack (SpriteBatchRenderer's
/// unitRectangle is a flat-color quad, same constraint BorderRenderer/MapTintGrid work
/// within), so the fade is approximated with GlowRingCount concentric 1px rings expanding
/// outward from bounds, reusing BorderThickness.GetEdgeRectangles the same way BorderRenderer
/// does but against an inflated rectangle instead of an inset one.
/// </summary>
public static class GlowRenderer
{
    private const int GlowRingCount = 7;
    private const float MaximumAlpha = 0.7f;

    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Color glowColor)
    {
        for (var distance = 1; distance <= GlowRingCount; distance++)
        {
            var ringBounds = new Rectangle(
                bounds.X - distance,
                bounds.Y - distance,
                bounds.Width + distance * 2,
                bounds.Height + distance * 2);

            // distance=1 (just outside the border) -> MaximumAlpha, distance=GlowRingCount -> 0.
            var alpha = MaximumAlpha * (1f - (distance - 1) / (float)(GlowRingCount - 1));
            var ringColor = glowColor * alpha;

            var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(ringBounds, BorderThickness.Uniform(Vector2.One));
            spriteBatch.Draw(unitRectangle, top, ringColor);
            spriteBatch.Draw(unitRectangle, bottom, ringColor);
            spriteBatch.Draw(unitRectangle, left, ringColor);
            spriteBatch.Draw(unitRectangle, right, ringColor);
        }
    }
}
