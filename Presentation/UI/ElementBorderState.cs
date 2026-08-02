using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Border bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale.</summary>
internal sealed class ElementBorderState
{
    public bool Show;
    public BorderThickness Thickness;
    public BorderStyle Style;

    /// <summary>
    /// The four edge strips a border draws as (see Element.RecalculateBorderRectangles) --
    /// four independently-addressable rectangles, not one solid rectangle, so a future 3D
    /// inset/outset treatment (see Window Chrome TODO) can shade each edge differently.
    /// </summary>
    public Rectangle TopRectangle;
    public Rectangle BottomRectangle;
    public Rectangle LeftRectangle;
    public Rectangle RightRectangle;
}
