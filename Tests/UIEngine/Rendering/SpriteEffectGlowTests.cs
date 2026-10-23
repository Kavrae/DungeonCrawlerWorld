using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class SpriteEffectGlowTests
{
    private const float Tolerance = 0.0001f;
    private static readonly Point TileSize = new(32, 24);

    private static Vector2 TexelPositionOf(Vector2 gridOrigin, int marginTiles, Vector2 drawPosition) =>
        SpriteEffectGlow.TexelPositionOf(SpriteEffectGlow.TransformFor(TileSize, gridOrigin, marginTiles), drawPosition);

    /// <summary>A tile's centre reads its texel's centre exactly, so linear filtering gives the tile its own glow there.</summary>
    [TestMethod]
    public void TileCentre_ReadsItsTexelsCentre()
    {
        var gridOrigin = new Vector2(-10, -7);

        var texelPosition = TexelPositionOf(gridOrigin, marginTiles: 3, gridOrigin + new Vector2(2.5f * TileSize.X, 1.5f * TileSize.Y));

        Assert.AreEqual(3 + 2.5f, texelPosition.X, Tolerance);
        Assert.AreEqual(3 + 1.5f, texelPosition.Y, Tolerance);
    }

    /// <summary>A tile's top-left corner is its texel's top-left corner: the whole tile reads inside its own texel.</summary>
    [TestMethod]
    public void TileCorner_ReadsItsTexelsCorner()
    {
        var gridOrigin = new Vector2(5, 0);

        var texelPosition = TexelPositionOf(gridOrigin, marginTiles: 0, gridOrigin + new Vector2(TileSize.X, 0));

        Assert.AreEqual(1f, texelPosition.X, Tolerance);
        Assert.AreEqual(0f, texelPosition.Y, Tolerance);
    }

    /// <summary>A tile left of the grid, in the margin, reads the texel before the grid's first.</summary>
    [TestMethod]
    public void TileInTheMargin_ReadsItsTexel()
    {
        var texelPosition = TexelPositionOf(Vector2.Zero, marginTiles: 3, new Vector2(-0.5f * TileSize.X, 0));

        Assert.AreEqual(2.5f, texelPosition.X, Tolerance);
    }

    [TestMethod]
    public void None_HasNoTexture() =>
        Assert.IsNull(SpriteEffectGlow.None.Texture);
}
