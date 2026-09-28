using Engine.Math;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class MapCameraTests
{
    /// <summary>36-pixel tiles in a 360-pixel view: 12 columns and rows, with the +2 partial-tile margin.</summary>
    private static MapCamera CameraOverCentredMap()
    {
        var camera = new MapCamera(TestWorlds.Create(new Map(new MapBounds(-1024, -1024, 1024, 1024, 1))));
        camera.Initialize(new Vector2(360, 360));
        return camera;
    }

    [TestMethod]
    public void CenterCameraOn_NearTheNegativeEdge_ClampsToTheMapMinimumNotZero()
    {
        var camera = CameraOverCentredMap();

        camera.CenterCameraOn(new Vector3Int(-1020, -1020, 0));

        Assert.AreEqual(new Point(-1024, -1024), camera.CurrentScrollPosition);
    }

    [TestMethod]
    public void UpdateScrollPosition_PastBothEdges_StopsAtMinimumAndMaximum()
    {
        var camera = CameraOverCentredMap();

        camera.UpdateScrollPosition(new Point(-5000, 5000));

        Assert.AreEqual(new Point(-1024, 1024 - camera.TileRows), camera.CurrentScrollPosition);
    }

    /// <summary>Half a tile of drag west of a negative scroll position lands one whole tile further west plus half a tile of offset -- the whole-tile part floors rather than truncating toward zero, so the offset never goes negative.</summary>
    [TestMethod]
    public void ApplyDrag_AtNegativeScroll_FloorsToWholeTilesAndKeepsTheOffsetPositive()
    {
        var camera = CameraOverCentredMap();
        camera.UpdateScrollPosition(new Point(-1000, -1000));
        camera.BeginDrag();

        var delta = camera.ApplyDrag(new Vector2(18, 0));

        Assert.AreEqual(new Point(-1, 0), delta);
        Assert.AreEqual(18f, camera.RenderPixelOffset.X);
    }

    /// <summary>The sliding window changes the map's bounds as neighborhoods load and unload; refreshing the limits lets the camera follow them, and pulls it back inside when they shrink.</summary>
    [TestMethod]
    public void RefreshScrollLimits_FollowsTheMapsBoundsAsNeighborhoodsLoadAndUnload()
    {
        var map = Map.Unbounded(depth: 1);
        map.LoadNeighborhood(0, 0);
        var camera = new MapCamera(TestWorlds.Create(map));
        camera.Initialize(new Vector2(360, 360));

        map.LoadNeighborhood(1, 0);
        camera.UpdateScrollPosition(new Point(5000, 0));
        Assert.AreEqual(1024 - camera.TileColumns, camera.CurrentScrollPosition.X, "Still the old limits.");

        camera.RefreshScrollLimits();
        camera.UpdateScrollPosition(new Point(5000, 0));
        Assert.AreEqual(2048 - camera.TileColumns, camera.CurrentScrollPosition.X);

        map.UnloadNeighborhood(1, 0);
        camera.RefreshScrollLimits();
        Assert.AreEqual(1024 - camera.TileColumns, camera.CurrentScrollPosition.X);
    }
}
