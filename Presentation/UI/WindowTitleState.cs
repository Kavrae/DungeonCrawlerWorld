using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Title bar bookkeeping -- see WindowGeometryState for why this is a plain-field class rather than a properties one.</summary>
internal sealed class WindowTitleState
{
    public bool ShowTitle;

    /// <summary>Whether the title bar still shows when the window is minimized (so title-less windows don't disappear entirely).</summary>
    public bool ShowWhenMinimized;

    public string Text = string.Empty;
    public Vector2 OriginalSize;
    public Vector2 Size;
    public Vector2 AbsolutePosition;
    public Rectangle Rectangle;
    public Color BackgroundColor;
    public Color FocusedBackgroundColor;
    public List<Button> Buttons = [];
}