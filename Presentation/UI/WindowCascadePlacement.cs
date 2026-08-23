using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Where a new child window (ItemDetailsWindow, a corpse loot window, AbilityScoreWindow, an Item
/// Details Comparison column, ...) should open relative to its own anchor window. The first sibling
/// (siblingCount 0) opens directly right of the anchor -- the same fixed-gap idiom every one of
/// these call sites already used individually (each with its own separately-declared `Gap = 12f`,
/// now unified here). Each additional sibling nudges diagonally down-and-right by CascadeStep from
/// that same base point, mirroring NotificationCenter's own ActiveNotificationStackOffset cascade --
/// so a growing family (Item Details Comparison's own columns are the only place today more than
/// one sibling exists at once) no longer runs the position further right by a full window-width per
/// addition, which is what let a third-or-later comparison column spawn off-screen previously.
/// Always clamped to screenSize (see ScreenBoundsClamp), so nothing this places can end up
/// off-screen regardless of how far the anchor itself has been dragged or how many siblings exist.
/// </summary>
public static class WindowCascadePlacement
{
    public const float Gap = 12f;
    private const float CascadeStep = 10f;

    public static Vector2 ComputePosition(Rectangle anchor, Vector2 childSize, int siblingCount, Vector2 screenSize)
    {
        var position = new Vector2(anchor.Right + Gap, anchor.Top) + new Vector2(CascadeStep, CascadeStep) * siblingCount;
        return ScreenBoundsClamp.Clamp(position, childSize, screenSize);
    }
}
