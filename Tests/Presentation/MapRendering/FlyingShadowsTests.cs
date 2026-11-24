using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class FlyingShadowsTests
{
    private const int Ground = (int)MapLayer.Ground;
    private const int Flying = (int)MapLayer.Flying;

    [TestMethod]
    [DataRow((byte)1, EntitySizeCategory.Normal, "Something is in the sky above this tile.")]
    [DataRow((byte)2, EntitySizeCategory.Large, "Something large is in the sky above this tile.")]
    [DataRow((byte)3, EntitySizeCategory.Huge, "Something huge is in the sky above this tile.")]
    public void AFlyersFootprint_NamesItsSizeOnEveryGroundTileItCovers(byte footprintSide, EntitySizeCategory expectedSize, string expectedDescription)
    {
        var map = new FakeMapView();
        map.AddBlocking(20, 20, new Vector2Byte(footprintSide, footprintSide), layer: Flying);
        var farCorner = 20 + footprintSide - 1;

        Assert.IsTrue(FlyingShadows.TryGetLargestCasterSize(map, farCorner, farCorner, Ground, out var size));
        Assert.AreEqual(expectedSize, size);
        Assert.AreEqual(expectedDescription, FlyingShadows.DescriptionFor(size));
    }

    [TestMethod]
    public void SeveralFlyersOverOneTile_NameTheBiggest()
    {
        var map = new FakeMapView();
        map.AddNonBlocking(20, 20, new Vector2Byte(1, 1), NonBlockingKind.Phasing, layer: Flying);
        map.AddBlocking(19, 19, new Vector2Byte(2, 2), layer: Flying);

        Assert.IsTrue(FlyingShadows.TryGetLargestCasterSize(map, 20, 20, Ground, out var size));
        Assert.AreEqual(EntitySizeCategory.Large, size);
    }

    [TestMethod]
    public void ATileWithNothingFlyingAboveIt_HasNoShadow_EvenWithSomethingOnGround()
    {
        var map = new FakeMapView();
        map.AddBlocking(20, 20, layer: Ground);
        map.AddBlocking(22, 20, layer: Flying);

        Assert.IsFalse(FlyingShadows.TryGetLargestCasterSize(map, 20, 20, Ground, out _));
    }

    [TestMethod]
    [DataRow((int)MapLayer.UnderGround)]
    [DataRow((int)MapLayer.Flying)]
    public void WhileGroundIsNotShown_NoTileHasAShadow(int mapLayer)
    {
        var map = new FakeMapView();
        map.AddBlocking(20, 20, layer: Flying);
        map.AddBlocking(20, 20, layer: Ground);

        Assert.IsFalse(FlyingShadows.TryGetLargestCasterSize(map, 20, 20, mapLayer, out _));
    }
}
