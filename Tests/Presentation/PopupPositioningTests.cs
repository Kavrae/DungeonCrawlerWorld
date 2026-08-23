using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class PopupPositioningTests
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 1000, 1000);

    [TestMethod]
    public void GetPositionWithinBounds_PreferredAnchorFits_MatchesGetPosition()
    {
        var target = new Rectangle(100, 100, 50, 50);
        var popupSize = new Vector2(60, 40);

        var expected = PopupPositioning.GetPosition(target, popupSize, PopupAnchor.East, Vector2.Zero);
        var result = PopupPositioning.GetPositionWithinBounds(target, popupSize, PopupAnchor.East, Vector2.Zero, ScreenBounds);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GetPositionWithinBounds_EastClipsRightEdge_FlipsToWest()
    {
        var target = new Rectangle(950, 100, 40, 40);
        var popupSize = new Vector2(60, 40);

        var result = PopupPositioning.GetPositionWithinBounds(target, popupSize, PopupAnchor.East, Vector2.Zero, ScreenBounds);
        var expected = PopupPositioning.GetPosition(target, popupSize, PopupAnchor.West, Vector2.Zero);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GetPositionWithinBounds_NorthClipsTopEdge_FlipsToSouth()
    {
        var target = new Rectangle(500, 10, 40, 40);
        var popupSize = new Vector2(40, 60);

        var result = PopupPositioning.GetPositionWithinBounds(target, popupSize, PopupAnchor.North, Vector2.Zero, ScreenBounds);
        var expected = PopupPositioning.GetPosition(target, popupSize, PopupAnchor.South, Vector2.Zero);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GetPositionWithinBounds_ClipsBothAxes_FlipsBoth()
    {
        var target = new Rectangle(960, 10, 30, 30);
        var popupSize = new Vector2(60, 60);

        var result = PopupPositioning.GetPositionWithinBounds(target, popupSize, PopupAnchor.NorthEast, Vector2.Zero, ScreenBounds);
        var expected = PopupPositioning.GetPosition(target, popupSize, PopupAnchor.SouthWest, Vector2.Zero);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GetPositionWithinBounds_PopupLargerThanScreen_StaysClampedAndDoesNotThrow()
    {
        var target = new Rectangle(100, 100, 20, 20);
        var popupSize = new Vector2(2000, 2000);

        var result = PopupPositioning.GetPositionWithinBounds(target, popupSize, PopupAnchor.East, Vector2.Zero, ScreenBounds);

        Assert.AreEqual(0f, result.X);
        Assert.AreEqual(0f, result.Y);
    }
}
