using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Auras;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using SpriteFormat;
using Tests.UIEngine;
using UIEngine.Rendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
[DoNotParallelize]
public sealed class MapRendererTests
{
    private static readonly Point Scroll = new(100, 200);
    private const int TileColumns = 10;
    private const int TileRows = 8;

    private static int ColumnsBeside => SpriteReach.MaximumColumnsBeside + 1;
    private static int RowsBelow => SpriteReach.MaximumRowsAbove + 1;

    /// <summary>36-pixel tiles in a 360-pixel view over a map centred on the origin.</summary>
    private static MapCamera CameraOverCentredMap()
    {
        var camera = TestMapCameras.Over(TestWorlds.Create(new Map(new MapBounds(-1024, -1024, 1024, 1024, 2))));
        camera.Initialize(new Vector2(360, 360));
        return camera;
    }

    private static bool Touches(int minX, int minY, int maxX, int maxY) =>
        MapRenderer.TouchesDrawnArea(new MapBounds(minX, minY, maxX, maxY, 1), Scroll, TileColumns, TileRows);

    [TestMethod]
    public void ImageKey_WholeTileScroll_ChangesIt()
    {
        var camera = CameraOverCentredMap();
        var before = MapImageKey.Of(camera, 0);

        camera.UpdateScrollPosition(new Point(1, 0));

        Assert.AreNotEqual(before, MapImageKey.Of(camera, 0));
    }

    [TestMethod]
    public void ImageKey_Zoom_ChangesIt()
    {
        var camera = CameraOverCentredMap();
        var before = MapImageKey.Of(camera, 0);

        camera.UpdateZoomLevel(ZoomLevel.Neighborhood, new Vector2(360, 360));

        Assert.AreNotEqual(before, MapImageKey.Of(camera, 0));
    }

    [TestMethod]
    public void ImageKey_MapLayer_ChangesIt()
    {
        var camera = CameraOverCentredMap();

        Assert.AreNotEqual(MapImageKey.Of(camera, 0), MapImageKey.Of(camera, 1));
    }

    /// <summary>A drag's sub-tile offset is where the image is blitted, not what it shows, so dragging less than a tile rebuilds nothing.</summary>
    [TestMethod]
    public void ImageKey_SubTileDrag_LeavesItUnchanged()
    {
        var camera = CameraOverCentredMap();
        var before = MapImageKey.Of(camera, 0);

        camera.BeginDrag();
        var scrollChange = camera.ApplyDrag(new Vector2(-10, 0));

        Assert.AreEqual(Point.Zero, scrollChange);
        Assert.AreNotEqual(Vector2.Zero, camera.RenderPixelOffset);
        Assert.AreEqual(before, MapImageKey.Of(camera, 0));
    }

    [TestMethod]
    public void TouchesDrawnArea_InsideTheGrid_Touches() =>
        Assert.IsTrue(Touches(105, 204, 106, 205));

    /// <summary>A wall up to the overscan beside the grid reaches into it, and one tile further decides whether a wall in it shows its face.</summary>
    [TestMethod]
    public void TouchesDrawnArea_BesideTheGrid_TouchesUpToTheOverscanPlusOne()
    {
        Assert.IsTrue(Touches(Scroll.X - ColumnsBeside, 204, Scroll.X - ColumnsBeside + 1, 205), "Left, at the overscan's edge.");
        Assert.IsFalse(Touches(Scroll.X - ColumnsBeside - 1, 204, Scroll.X - ColumnsBeside, 205), "Left, one past it.");
        Assert.IsTrue(Touches(Scroll.X + TileColumns + ColumnsBeside - 1, 204, Scroll.X + TileColumns + ColumnsBeside, 205), "Right, at the overscan's edge.");
        Assert.IsFalse(Touches(Scroll.X + TileColumns + ColumnsBeside, 204, Scroll.X + TileColumns + ColumnsBeside + 1, 205), "Right, one past it.");
    }

    /// <summary>Sprites reach up, never down: below the grid counts as far as a wall's reach plus one, above it nothing does.</summary>
    [TestMethod]
    public void TouchesDrawnArea_BelowTheGridToItsReachPlusOne_AboveItNot()
    {
        Assert.IsTrue(Touches(105, Scroll.Y + TileRows + RowsBelow - 1, 106, Scroll.Y + TileRows + RowsBelow), "Below, at the reach's edge.");
        Assert.IsFalse(Touches(105, Scroll.Y + TileRows + RowsBelow, 106, Scroll.Y + TileRows + RowsBelow + 1), "Below, one past it.");
        Assert.IsFalse(Touches(105, Scroll.Y - 1, 106, Scroll.Y), "The row above the grid.");
    }

    /// <summary>The renderer reads only the map view, so it builds and updates over a stand-in -- what a map editor hands it -- with no GraphicsDevice, drawing nothing until one exists.</summary>
    [TestMethod]
    public void OverAStandInMapView_BuildsAndUpdatesHeadlessly()
    {
        var mapView = new FakeMapView();
        mapView.AddWall(3, 3, "Wall");
        mapView.AddBlocking(4, 4);
        var camera = new MapCamera(mapView);
        camera.Initialize(new Vector2(360, 360));
        var labelRenderer = new LabelRenderer();
        var auras = TestAuras.GlowOnlyCatalog();
        var auraPower = new AuraPowerView(new AuraField(TestWorlds.Create(new Map(new Vector3Int(64, 64, 1))), new TerrainRegistry(), auras, new EventBus()), auras);

        var renderer = new MapRenderer(mapView, auraPower, camera, TestElementPoolServiceFactory.Create(TestFonts.Shared, labelRenderer), TestFonts.Shared, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), labelRenderer);

        renderer.Update(0);
        renderer.InvalidateTerrain();
        renderer.InvalidateTerrainIn(new MapBounds(0, 0, 1024, 1024, 1));
        camera.UpdateScrollPosition(new Point(5, 5));
        renderer.Update(0);
    }
}
