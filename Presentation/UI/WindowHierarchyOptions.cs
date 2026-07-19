namespace Presentation.UI;

/// <summary>How a window relates to its child windows.</summary>
public sealed class WindowHierarchyOptions
{
    public bool? CanContainChildWindows { get; set; }

    /// <summary>How child windows are tiled based on their position in the child window list.</summary>
    public WindowTileMode? ChildWindowTileMode { get; set; }
}
