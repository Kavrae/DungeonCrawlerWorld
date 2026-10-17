namespace SpriteFormat;

/// <summary>An area in tiles, relative to the top-left corner of a footprint, y down.</summary>
public readonly record struct SpriteTileArea(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;
}
