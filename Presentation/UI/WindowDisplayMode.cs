namespace Presentation.UI;

/// <summary>How a window sizes itself relative to its content and parent container.</summary>
public enum WindowDisplayMode
{
    /// <summary>Hides content; shows only the title bar if ShowTitleWhenMinimized is set.</summary>
    Minimized,

    /// <summary>Fixed size from options; used to determine text formatting.</summary>
    Static,

    /// <summary>Expands to fill the parent container's available content space.</summary>
    Fill,

    /// <summary>Grows to fit content, up to the parent's or a specified maximum size.</summary>
    Grow,
}
