using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Content-area bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale. Named _contentState, not _content, to avoid colliding with the pluggable IWindowContent field on Window.</summary>
internal sealed class ElementContentState
{
    public Vector2 AbsolutePosition;
    public Vector2 Size;
    public Rectangle Rectangle;
    public Color BackgroundColor;
}
