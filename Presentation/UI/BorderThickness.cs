using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Per-edge border thickness -- lets a window's border eventually have edges of differing
/// thickness (and, later, differing inset/outset shading per WindowChromeOptions' own
/// "3D borders with inset and outset modes" TODO) instead of one uniform Vector2 applied
/// identically to all four edges.
/// </summary>
public readonly record struct BorderThickness(float Left, float Top, float Right, float Bottom)
{
    public static BorderThickness Uniform(Vector2 size) => new(size.X, size.Y, size.X, size.Y);

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    /// <summary>
    /// The four edge strips a border of this thickness draws as within bounds -- top/bottom
    /// span the full width (covering the corners) while left/right are inset by that
    /// thickness so all four tile the outline without overlapping. Shared by Window (its own
    /// border) and Button (which used to hand-roll a single flat rectangle instead) so both
    /// get the same geometry for a future per-edge/inset-outset treatment.
    /// </summary>
    public static (Rectangle Top, Rectangle Bottom, Rectangle Left, Rectangle Right) GetEdgeRectangles(Rectangle bounds, BorderThickness thickness)
    {
        var topThickness = (int)thickness.Top;
        var bottomThickness = (int)thickness.Bottom;
        var leftThickness = (int)thickness.Left;
        var rightThickness = (int)thickness.Right;

        var top = new Rectangle(bounds.X, bounds.Y, bounds.Width, topThickness);
        var bottom = new Rectangle(bounds.X, bounds.Bottom - bottomThickness, bounds.Width, bottomThickness);
        var left = new Rectangle(bounds.X, bounds.Y + topThickness, leftThickness, bounds.Height - topThickness - bottomThickness);
        var right = new Rectangle(bounds.Right - rightThickness, bounds.Y + topThickness, rightThickness, bounds.Height - topThickness - bottomThickness);

        return (top, bottom, left, right);
    }

    /// <summary>bounds shrunk by this thickness on each corresponding side -- the space left over once a border of this thickness is drawn around the inside edge of bounds.</summary>
    public static Rectangle Inset(Rectangle bounds, BorderThickness thickness)
    {
        var left = (int)thickness.Left;
        var top = (int)thickness.Top;
        return new Rectangle(
            bounds.X + left,
            bounds.Y + top,
            bounds.Width - left - (int)thickness.Right,
            bounds.Height - top - (int)thickness.Bottom);
    }
}