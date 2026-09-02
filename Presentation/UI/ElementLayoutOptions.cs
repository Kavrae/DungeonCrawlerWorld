using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>A window's size/position/visibility, independent of its chrome or content.</summary>
public sealed class ElementLayoutOptions
{
    public ElementDisplayMode? DisplayMode { get; set; }

    /// <summary>Position relative to the parent's content area; relative to the screen if there is no parent.</summary>
    public Vector2? RelativePosition { get; set; }

    public Vector2? MinimumSize { get; set; }

    public Vector2? MaximumSize { get; set; }

    /// <summary>Size used when DisplayMode is Fixed. Defaults to the maximum size if unspecified.</summary>
    public Vector2? Size { get; set; }

    public bool? IsVisible { get; set; }

    public bool? IsTransparent { get; set; }

    /// <summary>Gap tiled after this element by RetileChildren, on top of its own size -- lets a caller reserve a blank gap before the next sibling in a Vertical/Horizontal-tiled parent without inserting a separate spacer element.</summary>
    public float? SpacingAfter { get; set; }
}