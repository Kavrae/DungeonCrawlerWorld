using Microsoft.Xna.Framework;
using UIEngine.Geometry;

namespace UIEngine.UI;

/// <summary>
/// Where a new child window (a details window, a comparison column, ...) should open relative to its own anchor window. The first sibling
/// (siblingCount 0) opens directly right of the anchor. Each additional sibling nudges diagonally down-and-right by CascadeStep from
/// that same base point, so a growing family of siblings doesn't march a full window-width right per addition.
/// Always clamped to screenSize (see RectangleClamp), so nothing this places can end up
/// off-screen regardless of how far the anchor itself has been dragged or how many siblings exist.
/// </summary>
public static class WindowCascadePlacement
{
    public const float Gap = 12f;
    private const float CascadeStep = 10f;

    public static Vector2 ComputePosition(Rectangle anchor, Vector2 childSize, int siblingCount, Vector2 screenSize)
    {
        var position = new Vector2(anchor.Right + Gap, anchor.Top) + new Vector2(CascadeStep, CascadeStep) * siblingCount;
        return RectangleClamp.ClampInside(position, childSize, screenSize);
    }
}
