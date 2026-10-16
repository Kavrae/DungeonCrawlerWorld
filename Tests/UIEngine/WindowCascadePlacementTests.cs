using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

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

        Assert.IsLessThanOrEqualTo(ScreenSize.X, result.X + childSize.X);
        Assert.IsLessThanOrEqualTo(ScreenSize.Y, result.Y + childSize.Y);
    }
}
