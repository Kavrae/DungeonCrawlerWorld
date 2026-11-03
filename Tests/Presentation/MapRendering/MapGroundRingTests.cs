using Microsoft.Xna.Framework;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapGroundRingTests
{
    [TestMethod]
    public void Ellipse_UnderOneTile_IsCentredAcrossIt_AndSitsInItsLowerHalf()
    {
        var (topLeft, size) = MapGroundRing.Ellipse(new Vector2(100, 200), new Vector2(40, 40));

        Assert.AreEqual(36f, size.X, 0.001f);
        Assert.AreEqual(18f, size.Y, 0.001f);
        Assert.AreEqual(102f, topLeft.X, 0.001f);
        Assert.AreEqual(238f, topLeft.Y + size.Y, 0.001f);
        Assert.IsGreaterThanOrEqualTo(220f, topLeft.Y);
    }

    [TestMethod]
    public void Ellipse_UnderALargeFootprint_ScalesWithIt()
    {
        var (topLeft, size) = MapGroundRing.Ellipse(new Vector2(0, 0), new Vector2(120, 120));

        Assert.AreEqual(108f, size.X, 0.001f);
        Assert.AreEqual(54f, size.Y, 0.001f);
        Assert.AreEqual(6f, topLeft.X, 0.001f);
        Assert.AreEqual(114f, topLeft.Y + size.Y, 0.001f);
    }

    [TestMethod]
    public void AlphaAt_IsStrongestAtTheRim_FaintInside_AndNothingOutside()
    {
        var rim = MapGroundRing.AlphaAt(0.95f, 32f);
        var centre = MapGroundRing.AlphaAt(0f, 32f);

        Assert.IsGreaterThan(centre, rim);
        Assert.IsGreaterThan(0f, centre);
        Assert.AreEqual(0f, MapGroundRing.AlphaAt(1f, 32f));
        Assert.IsLessThan(rim, MapGroundRing.AlphaAt(0.995f, 32f));
    }
}
