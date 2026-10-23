using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFormat;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class SpriteEffectQuadsTests
{
    private const float Tolerance = 0.001f;
    private static readonly Rectangle Destination = new(10, 20, 32, 32);
    private static readonly Rectangle WholeCell = new(0, 0, 16, 16);

    private static SpriteEffectVertex[] RectangleQuad(Rectangle destination, Rectangle? source, Point textureSize, Color color = default, SpriteDrawStyle style = default)
    {
        var corners = new SpriteEffectVertex[4];
        SpriteEffectQuads.Write(corners, new Vector2(destination.X, destination.Y), new Vector2(destination.Width, destination.Height), 0f, Vector2.Zero, SpriteEffectQuads.NormalizedSource(source, textureSize), color, SpriteEffects.None, style.ToVertexStyle());
        return corners;
    }

    /// <summary>The quad SpriteEffectBatch.Draw writes for SpriteRenderer's turned or flipped cell, from the same arguments SpriteBatch would be given.</summary>
    private static SpriteEffectVertex[] TransformedQuad(SpriteCellTransform transform)
    {
        var arguments = TransformedDrawArguments.For(transform, WholeCell, new Vector2(Destination.X, Destination.Y), new Vector2(Destination.Width, Destination.Height));
        var sourceSize = new Vector2(WholeCell.Width, WholeCell.Height);
        var corners = new SpriteEffectVertex[4];
        SpriteEffectQuads.Write(corners, arguments.Position, sourceSize * arguments.Scale, arguments.Rotation, arguments.Origin / sourceSize, SpriteEffectQuads.NormalizedSource(WholeCell, new Point(16, 16)), Color.White, arguments.Effects, default);
        return corners;
    }

    private static void AssertPosition(Vector2 expected, SpriteEffectVertex vertex, string corner)
    {
        Assert.AreEqual(expected.X, vertex.Position.X, Tolerance, $"{corner} x");
        Assert.AreEqual(expected.Y, vertex.Position.Y, Tolerance, $"{corner} y");
    }

    [TestMethod]
    public void RectangleDraw_CornersTopLeftTopRightBottomLeftBottomRight_OnTheDestination()
    {
        var corners = RectangleQuad(Destination, null, new Point(64, 64));

        AssertPosition(new Vector2(10, 20), corners[0], "top-left");
        AssertPosition(new Vector2(42, 20), corners[1], "top-right");
        AssertPosition(new Vector2(10, 52), corners[2], "bottom-left");
        AssertPosition(new Vector2(42, 52), corners[3], "bottom-right");
    }

    [TestMethod]
    public void RectangleDraw_SourceIsNormalizedByTheTexture()
    {
        var corners = RectangleQuad(Destination, new Rectangle(16, 32, 16, 8), new Point(64, 128));

        Assert.AreEqual(new Vector2(0.25f, 0.25f), corners[0].TextureCoordinate);
        Assert.AreEqual(new Vector2(0.5f, 0.25f), corners[1].TextureCoordinate);
        Assert.AreEqual(new Vector2(0.25f, 0.3125f), corners[2].TextureCoordinate);
        Assert.AreEqual(new Vector2(0.5f, 0.3125f), corners[3].TextureCoordinate);
    }

    [TestMethod]
    public void RectangleDraw_NoSource_IsTheWholeTexture()
    {
        var corners = RectangleQuad(Destination, null, new Point(64, 64));

        Assert.AreEqual(Vector2.Zero, corners[0].TextureCoordinate);
        Assert.AreEqual(Vector2.One, corners[3].TextureCoordinate);
    }

    [TestMethod]
    public void EveryCorner_CarriesTheColourAndTheStyle()
    {
        var colour = new Color(10, 20, 30, 40);
        var corners = RectangleQuad(Destination, null, new Point(64, 64), colour, SpriteDrawStyle.Plain);

        Assert.IsTrue(corners.All(corner => corner.Color == colour && corner.Style == SpriteDrawStyle.Plain.ToVertexStyle()));
    }

    /// <summary>SpriteBatch places an unrotated sprite at position minus its origin times its scale.</summary>
    [TestMethod]
    public void OriginAndScale_PlaceTheSpriteAsSpriteBatchDoes()
    {
        var corners = new SpriteEffectVertex[4];
        var origin = new Vector2(4, 2);
        var scale = new Vector2(2, 3);
        var sourceSize = new Vector2(16, 8);

        SpriteEffectQuads.Write(corners, new Vector2(100, 50), sourceSize * scale, 0f, origin / sourceSize, new Vector4(0, 0, 1, 1), Color.White, SpriteEffects.None, default);

        AssertPosition(new Vector2(100 - 4 * 2, 50 - 2 * 3), corners[0], "top-left");
        AssertPosition(new Vector2(100 - 8 + 32, 50 - 6 + 24), corners[3], "bottom-right");
    }

    /// <summary>A turned or flipped cell covers exactly the rectangle a plain one does (SpriteRenderer's promise), whichever way it is turned.</summary>
    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(90, false, false)]
    [DataRow(180, false, false)]
    [DataRow(270, false, false)]
    [DataRow(0, true, false)]
    [DataRow(0, false, true)]
    [DataRow(90, true, false)]
    [DataRow(270, false, true)]
    public void TransformedCell_CoversExactlyTheDestination(int rotation, bool flipHorizontal, bool flipVertical)
    {
        var corners = TransformedQuad(new SpriteCellTransform(rotation, flipHorizontal, flipVertical));

        Assert.AreEqual(Destination.Left, corners.Min(corner => corner.Position.X), Tolerance);
        Assert.AreEqual(Destination.Right, corners.Max(corner => corner.Position.X), Tolerance);
        Assert.AreEqual(Destination.Top, corners.Min(corner => corner.Position.Y), Tolerance);
        Assert.AreEqual(Destination.Bottom, corners.Max(corner => corner.Position.Y), Tolerance);
    }

    /// <summary>Which corner of the cell lands on the screen's top-left: flips swap texture coordinates, turns are clockwise on screen.</summary>
    [TestMethod]
    [DataRow(0, false, false, 0f, 0f)]
    [DataRow(0, true, false, 1f, 0f)]
    [DataRow(0, false, true, 0f, 1f)]
    [DataRow(90, false, false, 0f, 1f)]
    [DataRow(180, false, false, 1f, 1f)]
    [DataRow(270, false, false, 1f, 0f)]
    public void TransformedCell_ShowsTheExpectedCornerAtTheScreensTopLeft(int rotation, bool flipHorizontal, bool flipVertical, float expectedU, float expectedV)
    {
        var corners = TransformedQuad(new SpriteCellTransform(rotation, flipHorizontal, flipVertical));

        var screenTopLeft = corners.MinBy(corner => corner.Position.X + corner.Position.Y);

        Assert.AreEqual(expectedU, screenTopLeft.TextureCoordinate.X, Tolerance);
        Assert.AreEqual(expectedV, screenTopLeft.TextureCoordinate.Y, Tolerance);
    }

    [TestMethod]
    public void RunLength_CountsOnlyTheSameObjectInARow()
    {
        var first = new object();
        var second = new object();
        object[] textures = [first, first, second, first];

        Assert.AreEqual(2, SpriteEffectQuads.RunLength<object>(textures, 0));
        Assert.AreEqual(1, SpriteEffectQuads.RunLength<object>(textures, 2));
        Assert.AreEqual(1, SpriteEffectQuads.RunLength<object>(textures, 3));
    }

    /// <summary>SpriteBatch's index pattern -- two triangles over each quad's four corners -- which its winding under the default cull mode assumes.</summary>
    [TestMethod]
    public void Indices_TwoTrianglesPerQuad_InSpriteBatchsOrder()
    {
        var indices = SpriteEffectQuads.Indices(2);

        CollectionAssert.AreEqual(new short[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 5, 7, 6 }, indices);
    }
}
