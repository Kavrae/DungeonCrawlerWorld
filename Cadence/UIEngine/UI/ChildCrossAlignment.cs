namespace UIEngine.UI;

/// <summary>Where a tiled parent places each child across its tiling axis: vertically in a Horizontal row, horizontally in a Vertical column.</summary>
public enum ChildCrossAlignment : byte
{
    /// <summary>At the top of a row, the left of a column.</summary>
    Start,

    /// <summary>Centred in the parent's content, to the whole pixel.</summary>
    Center,

    /// <summary>At the bottom of a row, the right of a column.</summary>
    End,
}
