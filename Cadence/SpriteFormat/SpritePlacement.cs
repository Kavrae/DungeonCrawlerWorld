namespace SpriteFormat;

/// <summary>How a sprite's source rectangle maps onto its footprint: its size from PixelsPerTile, its position from the pivot landing on the footprint's bottom-centre.</summary>
/// <param name="PivotX">Source pixels from the source rectangle's left edge.</param>
/// <param name="PivotY">Source pixels from the source rectangle's top edge.</param>
public readonly record struct SpritePlacement(int PixelsPerTile, int PivotX, int PivotY);
