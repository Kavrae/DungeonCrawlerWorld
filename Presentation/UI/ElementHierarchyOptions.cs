namespace Presentation.UI;

/// <summary>How a window relates to its child windows.</summary>
public sealed class ElementHierarchyOptions
{
    public bool? CanContainChildren { get; set; }

    /// <summary>How child windows are tiled based on their position in the child window list.</summary>
    public ChildElementTileMode? ChildrenTileMode { get; set; }
}