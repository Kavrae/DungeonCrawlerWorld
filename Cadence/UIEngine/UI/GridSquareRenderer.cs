using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.UI.ColorPalettes;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>A grid square's interaction state -- see GridSquareRenderer.DrawStateOverlay.</summary>
public enum GridSquareState
{
    Normal,
    Hovered,
    Selected,
}

/// <summary>
/// Shared base fill + hover/selected styling for a grid-cell-shaped tile, one visual language for item cells and map tiles alike: a flat GridSquareBase fill,
/// a full white interior glow on hover, a light-blue interior-fade glow when selected.
/// </summary>
public static class GridSquareRenderer
{
    public static void DrawBase(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Color? baseColorOverride = null)
    {
        spriteBatch.Draw(unitRectangle, bounds, baseColorOverride ?? WindowPalette.GridSquareBase);
    }

    public static void DrawStateOverlay(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, GridSquareState state)
    {
        switch (state)
        {
            case GridSquareState.Hovered:
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, WindowPalette.Hover, GlowMode.InteriorFull);
                break;
            case GridSquareState.Selected:
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, WindowPalette.AttentionGlow, GlowMode.ExteriorFade);
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, WindowPalette.AttentionGlow, GlowMode.InteriorFade);
                break;
        }
    }
}
