using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class WindowCascadePlacementTests
{
    private static readonly Vector2 ScreenSize = new(1920, 1080);

    [TestMethod]
    public void ComputePosition_FirstSibling_PlacesDirectlyRightOfAnchor()
    {
        var anchor = new Rectangle(100, 100, 200, 200);

        var result = WindowCascadePlacement.ComputePosition(anchor, new Vector2(150, 150), siblingCount: 0, ScreenSize);

        Assert.AreEqual(new Vector2(anchor.Right + WindowCascadePlacement.Gap, anchor.Top), result);
    }

    [TestMethod]
    public void ComputePosition_LaterSiblings_CascadeDiagonally()
    {
        var anchor = new Rectangle(100, 100, 200, 200);
        var basePosition = new Vector2(anchor.Right + WindowCascadePlacement.Gap, anchor.Top);

        var result = WindowCascadePlacement.ComputePosition(anchor, new Vector2(150, 150), siblingCount: 2, ScreenSize);

        Assert.AreEqual(basePosition + new Vector2(20, 20), result);
    }

    [TestMethod]
    public void ComputePosition_NearRightScreenEdge_ClampsInsteadOfRunningOff()
    {
        var anchor = new Rectangle((int)ScreenSize.X - 220, 100, 200, 200);
        var childSize = new Vector2(150, 150);

        var result = WindowCascadePlacement.ComputePosition(anchor, childSize, siblingCount: 5, ScreenSize);

        Assert.IsTrue(result.X + childSize.X <= ScreenSize.X);
        Assert.IsTrue(result.Y + childSize.Y <= ScreenSize.Y);
    }
}

[TestClass]
public sealed class ScreenBoundsClampTests
{
    [TestMethod]
    public void Clamp_AlreadyInBounds_ReturnsUnchanged()
    {
        var result = ScreenBoundsClamp.Clamp(new Vector2(50, 50), new Vector2(100, 100), new Vector2(1000, 1000));

        Assert.AreEqual(new Vector2(50, 50), result);
    }

    [TestMethod]
    public void Clamp_PastRightEdge_PullsBackToFit()
    {
        var result = ScreenBoundsClamp.Clamp(new Vector2(950, 50), new Vector2(100, 100), new Vector2(1000, 1000));

        Assert.AreEqual(900, result.X);
    }

    [TestMethod]
    public void Clamp_OversizedChild_DoesNotInvertRangeOrThrow()
    {
        var result = ScreenBoundsClamp.Clamp(new Vector2(50, 50), new Vector2(2000, 2000), new Vector2(1000, 1000));

        Assert.AreEqual(Vector2.Zero, result);
    }
}
