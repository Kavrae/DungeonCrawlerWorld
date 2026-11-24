using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>A grid square's interaction state -- see GridSquareRenderer.DrawStateOverlay.</summary>
public enum GridSquareState
{
    Normal,
    Hovered,
    Selected,
    ContextMenuTarget,
}

/// <summary>
/// Shared base fill + hover/selected styling for a grid-cell-shaped tile, one visual language for item cells and map tiles alike: a flat GridSquareBase fill,
/// a full white interior glow on hover, a gold glow inside and out when selected, a sky-blue one inside and out while a context menu is open for it.
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
            case GridSquareState.ContextMenuTarget:
                GlowRenderer.Draw(spriteBatch, bounds, WindowPalette.ContextMenuTargetGlow, GlowMode.ExteriorFade);
                GlowRenderer.Draw(spriteBatch, bounds, WindowPalette.ContextMenuTargetGlow, GlowMode.InteriorFade);
                break;
        }
    }
}
