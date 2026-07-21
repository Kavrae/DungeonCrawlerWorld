using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Content-area bookkeeping -- see WindowGeometryState for why this is a plain-field class rather than a properties one.</summary>
internal sealed class WindowContentState
{
    public Vector2 AbsolutePosition;
    public Vector2 Size;
    public Rectangle Rectangle;
    public Color BackgroundColor;
}
