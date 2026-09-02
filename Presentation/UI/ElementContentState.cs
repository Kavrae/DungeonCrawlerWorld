using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Content-area bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale. Named _contentState, not _content, to avoid colliding with the pluggable IWindowContent field on Window.</summary>
internal sealed class ElementContentState
{
    public Vector2 AbsolutePosition;
    public Vector2 Size;
    public Rectangle Rectangle;
    public Color BackgroundColor;

    /// <summary>
    /// The content area before ChildContentPadding is carved out of it -- what AbsolutePosition/
    /// Size/Rectangle used to mean before padding existed. BackgroundRectangle (below) is drawn
    /// with BackgroundColor instead of Rectangle, so an element's own fill still covers its full
    /// content area edge-to-edge; only where CHILDREN get positioned/sized is inset by padding.
    /// Without this split, the padding band itself would go unpainted -- a visible gap of
    /// whatever's behind this element, between its border and its (now-inset) content fill.
    /// </summary>
    public Vector2 BackgroundAbsolutePosition;
    public Vector2 BackgroundSize;
    public Rectangle BackgroundRectangle;
}
