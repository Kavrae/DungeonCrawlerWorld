using Microsoft.Xna.Framework;

namespace UIEngine.UI;

/// <summary>A window's size/position/visibility, independent of its chrome or content.</summary>
public sealed class ElementLayoutOptions
{
    public ElementDisplayMode? DisplayMode { get; init; }

    /// <summary>Position relative to the parent's content area; relative to the screen if there is no parent.</summary>
    public Vector2? RelativePosition { get; init; }

    public Vector2? MinimumSize { get; init; }

    public Vector2? MaximumSize { get; init; }

    /// <summary>Size used when DisplayMode is Fixed. Defaults to the maximum size if unspecified.</summary>
    public Vector2? Size { get; init; }

    public bool? IsVisible { get; init; }

    public bool? IsTransparent { get; init; }

    /// <summary>Gap tiled after this element by RetileChildren, on top of its own size -- lets a caller reserve a blank gap before the next sibling in a Vertical/Horizontal-tiled parent without inserting a separate spacer element.</summary>
    public float? SpacingAfter { get; init; }
}