using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

/// <summary>Which shape a glow takes -- see GlowRenderer.Draw.</summary>
public enum GlowMode
{
    /// <summary>A single flat overlay at half the glow color's saturation across the entire bounds -- no rings.</summary>
    InteriorFull,

    /// <summary>Rings fading inward from the border toward the center of bounds.</summary>
    InteriorFade,

    /// <summary>Rings fading outward from the border away from bounds.</summary>
    ExteriorFade,
}

/// <summary>
/// Draws a glow around or within a rectangle -- shared by any Window that opts in via SetGlow.
/// No shader/gradient support exists in this renderer stack (SpriteBatchRenderer's
/// unitRectangle is a flat-color quad, same constraint BorderRenderer/MapTintGrid work
/// within), so InteriorFade/ExteriorFade approximate the fade with FadeRingCount concentric
/// 1px rings, reusing BorderThickness.GetEdgeRectangles the same way BorderRenderer does, against
/// an inset (InteriorFade) or inflated (ExteriorFade) rectangle.
/// </summary>
public static class GlowRenderer
{
    private const int FadeRingCount = 5;
    private const float FadeMaximumAlpha = 0.5f;

    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Color glowColor, GlowMode mode = GlowMode.ExteriorFade, float alphaMultiplier = 1f)
    {
        switch (mode)
        {
            case GlowMode.InteriorFull:
                spriteBatch.Draw(unitRectangle, bounds, glowColor * (FadeMaximumAlpha * alphaMultiplier));
                break;
            case GlowMode.InteriorFade:
                DrawFadeRings(spriteBatch, unitRectangle, bounds, glowColor, alphaMultiplier, inward: true);
                break;
            case GlowMode.ExteriorFade:
                DrawFadeRings(spriteBatch, unitRectangle, bounds, glowColor, alphaMultiplier, inward: false);
                break;
        }
    }

    // ringIndex=1 (closest to the border) -> FadeMaximumAlpha, and the falloff continues linearly
    // such that a hypothetical ring FadeRingCount+1 would land exactly on 0.
    private static float RingAlpha(int ringIndex) => FadeMaximumAlpha * (1f - (ringIndex - 1) / (float)FadeRingCount);

    private static void DrawFadeRings(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Color glowColor, float alphaMultiplier, bool inward)
    {
        for (var ringIndex = 1; ringIndex <= FadeRingCount; ringIndex++)
        {
            var ringBounds = inward
                ? BorderThickness.Inset(bounds, BorderThickness.Uniform(new Vector2(ringIndex, ringIndex)))
                : new Rectangle(
                    bounds.X - ringIndex,
                    bounds.Y - ringIndex,
                    bounds.Width + ringIndex * 2,
                    bounds.Height + ringIndex * 2);

            var ringColor = glowColor * (RingAlpha(ringIndex) * alphaMultiplier);

            var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(ringBounds, BorderThickness.Uniform(Vector2.One));
            spriteBatch.Draw(unitRectangle, top, ringColor);
            spriteBatch.Draw(unitRectangle, bottom, ringColor);
            spriteBatch.Draw(unitRectangle, left, ringColor);
            spriteBatch.Draw(unitRectangle, right, ringColor);
        }
    }
}
