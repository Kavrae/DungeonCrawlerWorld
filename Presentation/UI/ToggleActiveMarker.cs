using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>The one marker for "this toggle is on", shared by a lit item's inventory cell, its hotbar slot and a toggled-on action's hotbar slot, so the three always read the same.</summary>
/// <remarks>An inner fading glow in WindowPalette.ToggleActiveGlow, stronger than an ordinary glow so it reads over the icon's own background. Drawn after the icon and before any selected or armed glow, which sit on top of it.</remarks>
public static class ToggleActiveMarker
{
    private const float GlowStrength = 1.6f;

    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, float alpha = 1f) =>
        GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, WindowPalette.ToggleActiveGlow, GlowMode.InteriorFade, GlowStrength * alpha);
}
