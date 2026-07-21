using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Regression coverage for SelectMapNodes indexing off the map: _tileColumns/_tileRows are
/// sized off the visible viewport (see MapWindow's background/glyph resolution), which can
/// be larger than the actual map, so a click can land inside the viewport but past the
/// map's real edge. SelectMapNodes must reject that rather than handing SelectionWindowContent
/// an out-of-bounds SelectedMapNodePosition (which crashed with an IndexOutOfRangeException
/// on Map.MapNodes before this fix).
/// </summary>
[TestClass]
public sealed class MapWindowTests
{
    private static (Game.World.World World, MapWindow MapWindow) BuildMapWindow(int mapSizeX, int mapSizeY, int mapSizeZ)
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(mapSizeX, mapSizeY, mapSizeZ)));
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService);

        var componentManager = new ComponentManager(100, 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref TransformComponent existing, TransformComponent incoming) => existing = incoming);
        componentManager.RegisterDirectPool<GlyphComponent>(static (ref GlyphComponent existing, GlyphComponent incoming) => existing = incoming);
        componentManager.RegisterDirectPool<BackgroundComponent>(static (ref BackgroundComponent existing, BackgroundComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<OccupancyComponent>(static (ref OccupancyComponent existing, OccupancyComponent incoming) => existing = incoming);

        windowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            fontService, windowService, world, componentManager, new TileRenderer(), new GlyphRenderer()));

        var mapWindow = windowService.CreateWindow<MapWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(1256, 776), DisplayMode = WindowDisplayMode.Static },
        });
        mapWindow.Initialize();

        return (world, mapWindow);
    }

    [TestMethod]
    public void SelectMapNodes_ClickWithinViewportButPastMapEdge_DoesNotThrowAndLeavesSelectionUnset()
    {
        var (world, mapWindow) = BuildMapWindow(5, 5, 1);

        // Team zoom = 12px tiles; the viewport (1256px content / 12 + 1 = 105 columns) is
        // far larger than this 5-wide map, so tile column 10 is visible but off the map.
        mapWindow.SelectMapNodes(new Point(10 * 12 + 1, 1));

        Assert.IsNull(world.SelectedMapNodePosition);
    }

    [TestMethod]
    public void SelectMapNodes_ClickOnMap_SetsSelection()
    {
        var (world, mapWindow) = BuildMapWindow(5, 5, 1);

        mapWindow.SelectMapNodes(new Point(1 * 12 + 1, 1 * 12 + 1));

        Assert.AreEqual(new Point(1, 1), world.SelectedMapNodePosition);
    }

    /// <summary>
    /// Regression test: Initialize called UpdateMaxScrollPosition before UpdateTileSizes, so
    /// max scroll was computed against a still-zero visible tile count -- the bound ended up
    /// as the full map width/height instead of (map size - visible tiles), letting the map
    /// scroll a whole extra viewport past its real edge (reported as "scrolls too far").
    /// With a 200-wide map and 105 visible columns at Team zoom (1256px content / 12px
    /// tiles + 1), the correct max scroll is 95, which puts the map's last column (199) at
    /// the window's rightmost visible column (index 104).
    /// </summary>
    [TestMethod]
    public void UpdateScrollPosition_ScrollingPastMax_StopsWithMapsLastColumnAtWindowsRightEdge()
    {
        var (world, mapWindow) = BuildMapWindow(200, 5, 1);

        mapWindow.UpdateScrollPosition(new Point(100_000, 0));
        mapWindow.SelectMapNodes(new Point(104 * 12 + 1, 1));

        Assert.AreEqual(new Point(199, 0), world.SelectedMapNodePosition);
    }

    /// <summary>
    /// Regression test: UpdateZoomLevel changed the visible tile count via UpdateTileSizes
    /// but never recalculated max scroll, so it went stale after any zoom change. Scrolling
    /// to Team zoom's max (95, see above), then zooming out to Borough (3px tiles -- the
    /// whole 200-wide map fits in the 1256px content area, so the correct max scroll is 0)
    /// must re-clamp the stale scroll position down to 0, not leave it at 95.
    /// </summary>
    [TestMethod]
    public void UpdateZoomLevel_RecalculatesMaxScrollAndReclampsCurrentPosition()
    {
        var (world, mapWindow) = BuildMapWindow(200, 5, 1);
        mapWindow.UpdateScrollPosition(new Point(100_000, 0));

        mapWindow.UpdateZoomLevel(ZoomLevel.Borough);
        mapWindow.SelectMapNodes(new Point(1, 1));

        Assert.AreEqual(new Point(0, 0), world.SelectedMapNodePosition);
    }

    [TestMethod]
    public void ChangeLayer_ClampsToValidRange()
    {
        // 3-deep map (UnderGround/Ground/Flying) -- MapWindow starts on Ground (index 1).
        // CurrentMapLayer lives on World (shared with SelectionWindowContent), not MapWindow.
        var (world, mapWindow) = BuildMapWindow(5, 5, 3);
        Assert.AreEqual(1, world.CurrentMapLayer);

        mapWindow.ChangeLayer(1);
        Assert.AreEqual(2, world.CurrentMapLayer);

        mapWindow.ChangeLayer(1);
        Assert.AreEqual(2, world.CurrentMapLayer, "Already at the topmost layer -- must not go past it.");

        mapWindow.ChangeLayer(-1);
        mapWindow.ChangeLayer(-1);
        Assert.AreEqual(0, world.CurrentMapLayer);

        mapWindow.ChangeLayer(-1);
        Assert.AreEqual(0, world.CurrentMapLayer, "Already at the bottommost layer -- must not go below it.");
    }
}
