using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Generic header-region bookkeeping shared by any Element that reserves a region above its
/// content, always drawn regardless of Minimized state when ShowHeaderWhenMinimized -- Window's
/// text title bar and Folder's icon button are both just a DrawHeader override over this same
/// sizing/visibility state. See ElementGeometryState's doc comment for the same "grouped, plain
/// fields" rationale. Window-specific header content (title text, buttons, colors) lives on
/// Window itself, not here -- this only holds what the generic Measure/Arrange/Draw pipeline
/// needs to reserve and position the region.
/// </summary>
internal sealed class ElementHeaderState
{
    public bool ShowHeader;
    public bool ShowHeaderWhenMinimized;
    public Vector2 OriginalSize;
    public Vector2 Size;
    public Vector2 AbsolutePosition;
    public Rectangle Rectangle;
}
