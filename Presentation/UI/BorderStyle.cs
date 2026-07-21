namespace Presentation.UI;

/// <summary>
/// How a border's four edges (see BorderThickness.GetEdgeRectangles) are shaded. Flat is a
/// single solid color on every edge, matching the original border look. Outset/Inset instead
/// give a light/dark two-tone bevel -- a raised or pressed 3D look -- see BorderRenderer for
/// which edges get which shade.
/// </summary>
public enum BorderStyle
{
    Flat,
    Outset,
    Inset,
}
