using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>What SpriteEffectBatch's TileOverlay draws read: a texture of TexelsPerTile texels per tile, a row per row of tiles, placed over a grid of tiles.</summary>
/// <remarks>
/// <para>
/// A tile's texels, left to right: a flat wash over the whole tile; up to FillsPerTile fills, each covering the tile from its
/// bottom up to a fraction of its height; a ring, faded inward from the tile's edge; and the fractions -- the fills' in red and
/// green, the ring's depth in pixels in blue. Colours are premultiplied, and the effect lays them over each other in that
/// order. A tile with nothing is transparent.
/// </para>
/// <para>
/// Transform maps a draw position to tile coordinates -- scale in X and Y, offset in Z and W -- so tile (0,0) spans [0, 1) on
/// both axes.
/// </para>
/// </remarks>
public readonly record struct SpriteEffectTileOverlay(Texture2D? Texture, Vector4 Transform, Vector2 TextureSize)
{
    public const int TexelsPerTile = 5;
    public const int WashTexel = 0;
    public const int FirstFillTexel = 1;
    public const int FillsPerTile = 2;
    public const int RingTexel = 3;
    public const int FractionsTexel = 4;

    public static SpriteEffectTileOverlay None => default;

    /// <param name="tileSize">A tile's size in draw space.</param>
    /// <param name="gridOrigin">Where the top-left corner of the texture's first tile is drawn, in draw space.</param>
    public static SpriteEffectTileOverlay OverTileGrid(Texture2D texture, Point tileSize, Vector2 gridOrigin) =>
        new(texture, SpriteEffectGlow.TransformFor(tileSize, gridOrigin, marginTiles: 0), new Vector2(texture.Width, texture.Height));

    /// <summary>A fill's fraction of its tile as the fractions texel holds it.</summary>
    public static byte FractionByte(float fraction) => (byte)MathF.Round(Math.Clamp(fraction, 0f, 1f) * byte.MaxValue);
}
