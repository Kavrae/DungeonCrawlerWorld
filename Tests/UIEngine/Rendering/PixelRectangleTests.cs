using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class PixelRectangleTests
{
    [TestMethod]
    public void TwoAreasMeetingAtAFractionalPixel_StillMeetOnceSnapped()
    {
        var tileSize = new Vector2(20.6f, 20.6f);

        var first = PixelRectangle.Snapped(new Vector2(10.6f, 0f), tileSize);
        var second = PixelRectangle.Snapped(new Vector2(10.6f + 20.6f, 0f), tileSize);

        Assert.AreEqual(first.Right, second.Left);
    }

    [TestMethod]
    public void AnAreaLeftOfTheOrigin_FloorsLikeOneToItsRight()
    {
        var snapped = PixelRectangle.Snapped(new Vector2(-0.5f, -0.5f), new Vector2(10f, 10f));

        Assert.AreEqual(new Rectangle(-1, -1, 10, 10), snapped);
    }
}
