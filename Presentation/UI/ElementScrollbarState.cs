using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Scrollbar bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale.</summary>
internal sealed class ElementScrollbarState
{
    public ScrollbarVisibility Visibility;
    public bool ShowVertical;
    public bool ShowHorizontal;

    public Rectangle VerticalTrackRectangle;
    public Rectangle VerticalThumbRectangle;
    public Rectangle HorizontalTrackRectangle;
    public Rectangle HorizontalThumbRectangle;

    /// <summary>The square where both tracks would meet, filled with the track color while both bars show.</summary>
    public Rectangle CornerRectangle;

    /// <summary>Set by UiInputController from its per-frame hover hit-test.</summary>
    public ScrollbarPart HoveredPart;

    /// <summary>Set by UiInputController while the mouse is held on a thumb or track.</summary>
    public ScrollbarPart PressedPart;
}
