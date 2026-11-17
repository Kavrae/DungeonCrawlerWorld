namespace UIEngine.UI;

/// <summary>How a window relates to its child windows.</summary>
public sealed class ElementHierarchyOptions
{
    public bool? CanContainChildren { get; init; }

    /// <summary>How child windows are tiled based on their position in the child window list.</summary>
    public ChildElementTileMode? ChildrenTileMode { get; init; }

    /// <summary>The space between each pair of tiled children, on top of each one's own SpacingAfter.</summary>
    public float? ChildGap { get; init; }

    /// <summary>Where tiled children sit across the tiling axis; Start when unset.</summary>
    public ChildCrossAlignment? ChildCrossAlignment { get; init; }
}