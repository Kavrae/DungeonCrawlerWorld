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
}
