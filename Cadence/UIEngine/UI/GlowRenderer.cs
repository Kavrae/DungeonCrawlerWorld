using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>Which shape a glow takes -- see GlowRenderer.Draw.</summary>
public enum GlowMode
{
    /// <summary>A single flat overlay at half the glow color's saturation across the entire bounds -- no rings.</summary>
    InteriorFull,

    /// <summary>A fade inward from the border toward the center of bounds.</summary>
    InteriorFade,

    /// <summary>A fade outward from the border away from bounds.</summary>
    ExteriorFade,
}

/// <summary>Draws a glow around or within a rectangle -- shared by any Window that opts in via SetGlow.</summary>
/// <remarks>Every mode is one quad of the unit rectangle; InteriorFade and ExteriorFade are faded by the sprite effect (SpriteDrawMode.FadeInward/FadeOutward) over FadeRingCount screen pixels, the outward one on bounds grown by that depth.</remarks>
public static class GlowRenderer
{
    /// <summary>How far in pixels InteriorFade/ExteriorFade extend from bounds' own edge -- public so a caller drawing text or other content right up against a glowing rectangle's edge can inset by this same amount and avoid sitting under the fade (see Tooltip.DrawContent's own band-table price column).</summary>
    public const int FadeRingCount = 5;

    /// <summary>The glow colour's alpha at its strongest -- public so something drawing the same glow another way (the map's tile overlay ring) matches it.</summary>
    public const float FadeMaximumAlpha = 0.5f;

    public static void Draw<TTarget>(TTarget target, Texture2D unitRectangle, Rectangle bounds, Color glowColor, GlowMode mode = GlowMode.ExteriorFade, float alphaMultiplier = 1f, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var color = glowColor * (FadeMaximumAlpha * alphaMultiplier);
        switch (mode)
        {
            case GlowMode.InteriorFull:
                target.Draw(unitRectangle, bounds, null, color, style);
                break;
            case GlowMode.InteriorFade:
                target.Draw(unitRectangle, bounds, null, color, style.WithFade(SpriteDrawMode.FadeInward, FadeRingCount));
                break;
            case GlowMode.ExteriorFade:
                var grownBounds = new Rectangle(bounds.X - FadeRingCount, bounds.Y - FadeRingCount, bounds.Width + FadeRingCount * 2, bounds.Height + FadeRingCount * 2);
                target.Draw(unitRectangle, grownBounds, null, color, style.WithFade(SpriteDrawMode.FadeOutward, FadeRingCount));
                break;
        }
    }
}
