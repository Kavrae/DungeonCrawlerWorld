using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

/// <summary>
/// Draws one full-screen translucent quad -- the visual half of UiLayerStack's menu mode concept
/// (see OpenMenuWindow/CloseMenuWindow), dimming whatever's behind the open menu-window set. Drawn
/// by ShellContext.Draw immediately beneath Layers.BottommostMenuWindow, so every open menu
/// window (not just the frontmost), plus every element UiLayerStack.IsMenuModeExempt opted out of
/// the dim, renders undimmed above it -- only what's actually blocked dims.
/// </summary>
public static class MenuModeDimRenderer
{
    private const float DimAlpha = 0.55f;
    private static readonly Color DimColor = Color.Black;

    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, GraphicsDevice graphicsDevice)
        => spriteBatch.Draw(unitRectangle, graphicsDevice.Viewport.Bounds, DimColor * DimAlpha);
}
