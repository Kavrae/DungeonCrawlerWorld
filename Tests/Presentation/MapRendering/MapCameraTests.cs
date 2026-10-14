using Engine.Math;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapCameraTests
{
    /// <summary>36-pixel tiles in a 360-pixel view: 12 columns and rows, with the +2 partial-tile margin.</summary>
    private static MapCamera CameraOverCentredMap()
    {
        var camera = TestMapCameras.Over(TestWorlds.Create(new Map(new MapBounds(-1024, -1024, 1024, 1024, 1))));
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
    public void RefreshScrollLimitsIfBoundsChanged_FollowsTheMapsBoundsAsNeighborhoodsLoadAndUnload()
    {
        var map = Map.Unbounded(depth: 1);
        map.LoadNeighborhood(0, 0);
        var camera = TestMapCameras.Over(TestWorlds.Create(map));
        camera.Initialize(new Vector2(360, 360));

        map.LoadNeighborhood(1, 0);
        camera.UpdateScrollPosition(new Point(5000, 0));
        Assert.AreEqual(1024 - camera.TileColumns, camera.CurrentScrollPosition.X, "Still the old limits.");

        Assert.IsTrue(camera.RefreshScrollLimitsIfBoundsChanged());
        camera.UpdateScrollPosition(new Point(5000, 0));
        Assert.AreEqual(2048 - camera.TileColumns, camera.CurrentScrollPosition.X);

        map.UnloadNeighborhood(1, 0);
        Assert.IsTrue(camera.RefreshScrollLimitsIfBoundsChanged());
        Assert.AreEqual(1024 - camera.TileColumns, camera.CurrentScrollPosition.X);
    }

    [TestMethod]
    public void RefreshScrollLimitsIfBoundsChanged_BoundsUnchangedSinceInitialize_ReportsNothingChanged()
    {
        var camera = CameraOverCentredMap();

        Assert.IsFalse(camera.RefreshScrollLimitsIfBoundsChanged());
    }

    /// <summary>The camera reads only the map view, so it works over a stand-in as well as a live session -- what a map editor needs.</summary>
    [TestMethod]
    public void OverAStandInMapView_ScrollsWithinItsBoundsAndResolvesHoveredTiles()
    {
        var camera = new MapCamera(new FakeMapView());
        camera.Initialize(new Vector2(360, 360));

        camera.UpdateScrollPosition(new Point(5000, -5000));
        Assert.AreEqual(new Point(1024 - camera.TileColumns, 0), camera.CurrentScrollPosition);

        Assert.IsTrue(camera.TryGetHoveredMapPosition(new Point(37, 37), Vector2.Zero, out var hovered));
        Assert.AreEqual(new Point(1024 - camera.TileColumns + 1, 1), hovered);
    }

    [TestMethod]
    public void TryGetTileRectangle_TileInsideTheGrid_IsItsScreenRectangle()
    {
        var camera = CameraOverCentredMap();
        camera.UpdateScrollPosition(new Point(10, 20));
        var scroll = camera.CurrentScrollPosition;

        Assert.IsTrue(camera.TryGetTileRectangle(scroll.X + 2, scroll.Y + 3, out var tileRectangle));
        Assert.AreEqual(new Rectangle(72, 108, 36, 36), tileRectangle);
    }

    [TestMethod]
    public void TryGetTileRectangle_TileOutsideTheGrid_IsRefused()
    {
        var camera = CameraOverCentredMap();
        var scroll = camera.CurrentScrollPosition;

        Assert.IsFalse(camera.TryGetTileRectangle(scroll.X - 1, scroll.Y, out _));
        Assert.IsFalse(camera.TryGetTileRectangle(scroll.X, scroll.Y + camera.TileRows, out _));
        Assert.IsTrue(camera.TryGetTileRectangle(scroll.X + camera.TileColumns - 1, scroll.Y + camera.TileRows - 1, out _));
    }

    [TestMethod]
    public void TryGetTileRectangle_MidDrag_ShiftsByTheSubTileOffset()
    {
        var camera = CameraOverCentredMap();
        camera.UpdateScrollPosition(new Point(1000, 1000));
        camera.BeginDrag();
        camera.ApplyDrag(new Vector2(-10, 0));
        var scroll = camera.CurrentScrollPosition;

        Assert.IsTrue(camera.TryGetTileRectangle(scroll.X + 1, scroll.Y, out var tileRectangle));
        Assert.AreEqual(new Rectangle(26, 0, 36, 36), tileRectangle);
    }
}
