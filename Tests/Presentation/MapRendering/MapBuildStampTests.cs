using Microsoft.Xna.Framework;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapBuildStampTests
{
    private static readonly MapImageKey Key = new(new Point(20, 30), TileColumns: 10, TileRows: 6, TileSize: new Point(32, 32), MapLayer: 0);

    [TestMethod]
    public void BeforeAnyBuild_IsStale()
    {
        Assert.IsFalse(new MapBuildStamp().IsCurrentFor(Key));
    }

    [TestMethod]
    public void AfterABuild_IsCurrentForThatKeyAndVersionOnly()
    {
        var stamp = new MapBuildStamp();

        stamp.Record(Key, version: 4);

        Assert.IsTrue(stamp.IsCurrentFor(Key, 4));
        Assert.IsFalse(stamp.IsCurrentFor(Key, 5));
        Assert.IsFalse(stamp.IsCurrentFor(Key with { Scroll = new Point(21, 30) }, 4));
        Assert.IsFalse(stamp.IsCurrentFor(Key with { MapLayer = 1 }, 4));
    }

    /// <summary>The camera leaves the grid a build was made for and comes back: the build is current again only if nothing else was built in between.</summary>
    [TestMethod]
    public void ReturningToAnEarlierKey_IsStale_WhenAnotherKeyWasBuiltSince()
    {
        var stamp = new MapBuildStamp();
        stamp.Record(Key);

        stamp.Record(Key with { Scroll = new Point(25, 30) });

        Assert.IsFalse(stamp.IsCurrentFor(Key));
    }

    [TestMethod]
    public void Invalidate_MakesTheLastBuildStale_UntilTheNextOne()
    {
        var stamp = new MapBuildStamp();
        stamp.Record(Key);

        stamp.Invalidate();

        Assert.IsFalse(stamp.IsCurrentFor(Key));
        stamp.Record(Key);
        Assert.IsTrue(stamp.IsCurrentFor(Key));
    }
}
