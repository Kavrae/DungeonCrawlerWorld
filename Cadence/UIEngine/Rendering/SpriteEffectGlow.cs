using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>The light SpriteEffectBatch's lit draws read (SpriteGlow): a texture of one texel per tile, premultiplied, placed over a grid of tiles.</summary>
/// <remarks>
/// Transform maps a draw position to texel coordinates -- scale in X and Y, offset in Z and W -- so a tile's centre lands on
/// its texel's centre and linear filtering shades between them. A texel left transparent is a tile with no light; the effect
/// lights nothing whose read point falls on one.
/// </remarks>
public readonly record struct SpriteEffectGlow(Texture2D? Texture, Vector4 Transform, Vector2 TextureSize)
{
    public static SpriteEffectGlow None => default;

    /// <param name="tileSize">A tile's size in draw space.</param>
    /// <param name="gridOrigin">Where tile (0,0)'s top-left corner is drawn, in draw space.</param>
    /// <param name="marginTiles">How many texels lie before tile (0,0) on each axis.</param>
    public static SpriteEffectGlow OverTileGrid(Texture2D texture, Point tileSize, Vector2 gridOrigin, int marginTiles) =>
        new(texture, TransformFor(tileSize, gridOrigin, marginTiles), new Vector2(texture.Width, texture.Height));

    /// <inheritdoc cref="OverTileGrid"/>
    public static Vector4 TransformFor(Point tileSize, Vector2 gridOrigin, int marginTiles) =>
        new(1f / tileSize.X, 1f / tileSize.Y, marginTiles - gridOrigin.X / tileSize.X, marginTiles - gridOrigin.Y / tileSize.Y);

    /// <summary>The texel coordinates transform maps drawPosition to -- what the effect reads at -- texel k spanning [k, k + 1).</summary>
    public static Vector2 TexelPositionOf(Vector4 transform, Vector2 drawPosition) =>
        new(drawPosition.X * transform.X + transform.Z, drawPosition.Y * transform.Y + transform.W);
}
