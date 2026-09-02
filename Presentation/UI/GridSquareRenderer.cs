using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>A grid square's interaction state -- see GridSquareRenderer.DrawStateOverlay.</summary>
public enum GridSquareState
{
    Normal,
    Hovered,
    Selected,
}

/// <summary>
/// Shared base fill + hover/selected styling for a grid-cell-shaped tile -- unifies what used to
/// be two independent implementations (InventoryItemStackCell's Cyan selected glow/Gold hover
/// overlay, MapWindow's own Gold tile wash) into one visual language: a flat GridSquareBase fill,
/// a full white interior glow on hover, a light-blue interior-fade glow when selected.
/// </summary>
public static class GridSquareRenderer
{
    public static void DrawBase(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Color? baseColorOverride = null)
    {
        spriteBatch.Draw(unitRectangle, bounds, baseColorOverride ?? WindowPalette.GridSquareBase);
    }

    public static void DrawStateOverlay(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, GridSquareState state)
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
