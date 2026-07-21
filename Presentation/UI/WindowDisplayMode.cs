namespace Presentation.UI;

/// <summary>
/// How a window sizes itself relative to its content and parent container -- names follow
/// the industry-standard trio for this concept (e.g. Qt's QSizePolicy::Fixed, Android's
/// WRAP_CONTENT): an explicit fixed size, filling the parent, or sizing to fit content.
/// </summary>
public enum WindowDisplayMode
{
    /// <summary>Hides content; shows only the title bar if ShowTitleWhenMinimized is set.</summary>
    Minimized,

    /// <summary>Fixed size from options; used to determine text formatting.</summary>
    Fixed,

    /// <summary>Expands to fill the parent container's available content space.</summary>
    Fill,

    /// <summary>Sizes to fit content, up to the parent's or a specified maximum size.</summary>
    WrapContent,
}
