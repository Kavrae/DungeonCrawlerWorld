using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Which side/corner of a target rectangle a Tooltip attaches to.</summary>
public enum PopupAnchor
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

/// <summary>
/// Positioning math shared by every anchored hover popup (see Tooltip) -- kept separate
/// from Tooltip itself since it's pure geometry, easily unit-testable without any
/// Element/GraphicsDevice machinery.
/// </summary>
public static class PopupPositioning
{
    /// <summary>
    /// Returns the absolute screen position for a popup's top-left corner, given the absolute
    /// Rectangle of whatever it's anchored to, the popup's own current size, which corner/side of
    /// the target it should attach to, and a gap (px) pushing it further away in that direction.
    /// E.g. NorthEast: the popup's bottom-left corner sits at (target.Right + gap.X, target.Top -
    /// gap.Y). North: the popup's bottom-center sits at (target.Center.X, target.Top - gap.Y).
    /// </summary>
    public static Vector2 GetPosition(Rectangle target, Vector2 popupSize, PopupAnchor anchor, Vector2 gap) => anchor switch
    {
        PopupAnchor.North => new Vector2(target.Center.X - popupSize.X / 2f, target.Top - gap.Y - popupSize.Y),
        PopupAnchor.South => new Vector2(target.Center.X - popupSize.X / 2f, target.Bottom + gap.Y),
        PopupAnchor.East => new Vector2(target.Right + gap.X, target.Center.Y - popupSize.Y / 2f),
        PopupAnchor.West => new Vector2(target.Left - gap.X - popupSize.X, target.Center.Y - popupSize.Y / 2f),
        PopupAnchor.NorthEast => new Vector2(target.Right + gap.X, target.Top - gap.Y - popupSize.Y),
        PopupAnchor.NorthWest => new Vector2(target.Left - gap.X - popupSize.X, target.Top - gap.Y - popupSize.Y),
        PopupAnchor.SouthEast => new Vector2(target.Right + gap.X, target.Bottom + gap.Y),
        PopupAnchor.SouthWest => new Vector2(target.Left - gap.X - popupSize.X, target.Bottom + gap.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null),
    };

    /// <summary>
    /// Tries `anchor` first; if the result would clip screenBounds on an axis, flips to the
    /// opposite side on that axis (East&lt;-&gt;West, North&lt;-&gt;South, and the matching
    /// diagonal pairs) and recomputes once, then clamps as a final safety net -- the standard
    /// tooltip/context-menu "try one side, flip if it doesn't fit" technique (near-universal in
    /// real UIs, unlike general collision-avoidance), not an open-ended search.
    /// </summary>
    public static Vector2 GetPositionWithinBounds(Rectangle target, Vector2 popupSize, PopupAnchor anchor, Vector2 gap, Rectangle screenBounds)
    {
        var position = GetPosition(target, popupSize, anchor, gap);

        if (ClipsHorizontally(position, popupSize, screenBounds))
        {
            anchor = FlipHorizontal(anchor);
            position = GetPosition(target, popupSize, anchor, gap);
        }

        if (ClipsVertically(position, popupSize, screenBounds))
        {
            anchor = FlipVertical(anchor);
            position = GetPosition(target, popupSize, anchor, gap);
        }

        return ScreenBoundsClamp.Clamp(position, popupSize, new Vector2(screenBounds.Width, screenBounds.Height));
    }

    private static bool ClipsHorizontally(Vector2 position, Vector2 size, Rectangle bounds) => position.X < bounds.Left || position.X + size.X > bounds.Right;

    private static bool ClipsVertically(Vector2 position, Vector2 size, Rectangle bounds) => position.Y < bounds.Top || position.Y + size.Y > bounds.Bottom;

    private static PopupAnchor FlipHorizontal(PopupAnchor anchor) => anchor switch
    {
        PopupAnchor.East => PopupAnchor.West,
        PopupAnchor.West => PopupAnchor.East,
        PopupAnchor.NorthEast => PopupAnchor.NorthWest,
        PopupAnchor.NorthWest => PopupAnchor.NorthEast,
        PopupAnchor.SouthEast => PopupAnchor.SouthWest,
        PopupAnchor.SouthWest => PopupAnchor.SouthEast,
        _ => anchor, // North/South have no horizontal component to flip.
    };

    private static PopupAnchor FlipVertical(PopupAnchor anchor) => anchor switch
    {
        PopupAnchor.North => PopupAnchor.South,
        PopupAnchor.South => PopupAnchor.North,
        PopupAnchor.NorthEast => PopupAnchor.SouthEast,
        PopupAnchor.SouthEast => PopupAnchor.NorthEast,
        PopupAnchor.NorthWest => PopupAnchor.SouthWest,
        PopupAnchor.SouthWest => PopupAnchor.NorthWest,
        _ => anchor, // East/West have no vertical component to flip.
    };
}
