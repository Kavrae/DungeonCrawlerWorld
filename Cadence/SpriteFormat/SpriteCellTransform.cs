namespace SpriteFormat;

/// <summary>How a manifest cell is turned and flipped when drawn: flips first, then Rotation clockwise about the centre -- the order a sprite batch applies flips and rotation in.</summary>
/// <param name="Rotation">0, 90, 180 or 270 degrees clockwise.</param>
public readonly record struct SpriteCellTransform(int Rotation, bool FlipHorizontal, bool FlipVertical)
{
    public static SpriteCellTransform None => default;

    public bool IsNone => Rotation == 0 && !FlipHorizontal && !FlipVertical;

    public bool IsQuarterTurn => Rotation is 90 or 270;

    public static SpriteCellTransform Of(SpriteManifestCell cell) => new(cell.Rotation, cell.FlipHorizontal, cell.FlipVertical);
}
