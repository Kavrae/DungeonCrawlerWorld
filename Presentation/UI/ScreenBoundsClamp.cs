using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Pulls a root element's screen-relative position back inside a screen of the given size (assumed
/// to start at the origin, matching a root element's own RelativePosition doubling as its absolute
/// screen position -- see Window.BuildWindow) so an element of `size` never ends up partly or fully
/// off-screen. Shared by UiInputController's own drag-move clamp (previously a private copy of this
/// exact math), WindowCascadePlacement, and PopupPositioning.GetPositionWithinBounds.
/// </summary>
public static class ScreenBoundsClamp
{
    public static Vector2 Clamp(Vector2 position, Vector2 size, Vector2 screenSize) => new(
        MathHelper.Clamp(position.X, 0, MathHelper.Max(0, screenSize.X - size.X)),
        MathHelper.Clamp(position.Y, 0, MathHelper.Max(0, screenSize.Y - size.Y)));
}
