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
}
