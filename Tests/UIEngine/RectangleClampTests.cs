using Microsoft.Xna.Framework;
using UIEngine.Geometry;

namespace Tests.UIEngine;

[TestClass]
public sealed class RectangleClampTests
{
    [TestMethod]
    public void ClampInside_AlreadyInBounds_ReturnsUnchanged()
    {
        var result = RectangleClamp.ClampInside(new Vector2(50, 50), new Vector2(100, 100), new Vector2(1000, 1000));

        Assert.AreEqual(new Vector2(50, 50), result);
    }

    [TestMethod]
    public void ClampInside_PastRightEdge_PullsBackToFit()
    {
        var result = RectangleClamp.ClampInside(new Vector2(950, 50), new Vector2(100, 100), new Vector2(1000, 1000));

        Assert.AreEqual(900, result.X);
    }

    [TestMethod]
    public void ClampInside_OversizedRectangle_PinsToBoundsMinimum()
    {
        var result = RectangleClamp.ClampInside(new Vector2(50, 50), new Vector2(2000, 2000), new Vector2(1000, 1000));

        Assert.AreEqual(Vector2.Zero, result);
    }

    [TestMethod]
    public void ClampInside_BoundsAwayFromOrigin_ClampsToEveryEdge()
    {
        var bounds = new Rectangle(200, 100, 400, 300);

        var beforeMinimum = RectangleClamp.ClampInside(new Vector2(0, 0), new Vector2(50, 50), bounds);
        var pastMaximum = RectangleClamp.ClampInside(new Vector2(1000, 1000), new Vector2(50, 50), bounds);

        Assert.AreEqual(new Vector2(200, 100), beforeMinimum);
        Assert.AreEqual(new Vector2(550, 350), pastMaximum);
    }

    [TestMethod]
    public void ClampInside_OversizedRectangleInBoundsAwayFromOrigin_PinsToBoundsMinimum()
    {
        var result = RectangleClamp.ClampInside(new Vector2(0, 0), new Vector2(1000, 1000), new Rectangle(200, 100, 400, 300));

        Assert.AreEqual(new Vector2(200, 100), result);
    }
}
