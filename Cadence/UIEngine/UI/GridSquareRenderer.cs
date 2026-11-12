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
    public static void DrawBase<TTarget>(TTarget spriteBatch, Rectangle bounds, Color? baseColorOverride = null)
        where TTarget : ISpriteDrawTarget
    {
        spriteBatch.FillRectangle(bounds, baseColorOverride ?? WindowPalette.GridSquareBase);
    }

    public static void DrawStateOverlay<TTarget>(TTarget spriteBatch, Rectangle bounds, GridSquareState state)
        where TTarget : ISpriteDrawTarget
    {
        switch (state)
        {
            case GridSquareState.Hovered:
                GlowRenderer.Draw(spriteBatch, bounds, WindowPalette.Hover, GlowMode.InteriorFull);
                break;
            case GridSquareState.Selected:
                GlowRenderer.Draw(spriteBatch, bounds, WindowPalette.AttentionGlow, GlowMode.ExteriorFade);
                GlowRenderer.Draw(spriteBatch, bounds, WindowPalette.AttentionGlow, GlowMode.InteriorFade);
                break;
        }
    }
}
