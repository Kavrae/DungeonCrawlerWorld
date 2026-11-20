using Microsoft.Xna.Framework;
using SpriteFormat;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class SpriteFitMathTests
{
    private static readonly Vector2 ContainerTopLeft = new(10, 20);
    private static readonly Vector2 ContainerSize = new(40, 40);

    [TestMethod]
    public void Contain_TallSource_FillsTheHeight_CentredAcross()
    {
        var (topLeft, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(16, 32), SpriteCellTransform.None);

        Assert.AreEqual(new Vector2(20, 40), size);
        Assert.AreEqual(new Vector2(20, 20), topLeft);
    }

    [TestMethod]
    public void Contain_WideSource_FillsTheWidth_OnTheBottomEdge()
    {
        var (topLeft, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(32, 16), SpriteCellTransform.None);

        Assert.AreEqual(new Vector2(40, 20), size);
        Assert.AreEqual(new Vector2(10, 40), topLeft);
    }

    [TestMethod]
    public void Contain_SquareSource_FillsTheSquare()
    {
        var (topLeft, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(16, 16), SpriteCellTransform.None);

        Assert.AreEqual(ContainerSize, size);
        Assert.AreEqual(ContainerTopLeft, topLeft);
    }

    [TestMethod]
    public void Contain_SourceLargerThanTheContainer_ShrinksToFit()
    {
        var (topLeft, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(64, 128), SpriteCellTransform.None);

        Assert.AreEqual(new Vector2(20, 40), size);
        Assert.AreEqual(new Vector2(20, 20), topLeft);
    }

    [TestMethod]
    public void Contain_QuarterTurnedTallSource_FitsAsWide()
    {
        var (topLeft, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(16, 32), new SpriteCellTransform(90, false, false));

        Assert.AreEqual(new Vector2(40, 20), size);
        Assert.AreEqual(new Vector2(10, 40), topLeft);
    }

    [TestMethod]
    public void Contain_HalfTurnOrFlip_KeepsTheSourceShape()
    {
        var (_, size) = SpriteFitMath.Contain(ContainerTopLeft, ContainerSize, new Point(16, 32), new SpriteCellTransform(180, true, false));

        Assert.AreEqual(new Vector2(20, 40), size);
    }

    [TestMethod]
    public void Contain_WideContainer_TallSource_CentredAcross()
    {
        var (topLeft, size) = SpriteFitMath.Contain(Vector2.Zero, new Vector2(80, 40), new Point(16, 16), SpriteCellTransform.None);

        Assert.AreEqual(new Vector2(40, 40), size);
        Assert.AreEqual(new Vector2(20, 0), topLeft);
    }

    [TestMethod]
    public void Fill_IsTheContainerUnchanged()
    {
        var fitted = SpriteFitMath.Fitted(SpriteFit.Fill, ContainerTopLeft, ContainerSize, new Point(16, 32), SpriteCellTransform.None);

        Assert.AreEqual((ContainerTopLeft, ContainerSize), fitted);
    }
}
