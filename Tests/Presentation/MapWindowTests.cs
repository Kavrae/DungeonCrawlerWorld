using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow) BuildMapWindow(int mapSizeX, int mapSizeY, int mapSizeZ)
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(mapSizeX, mapSizeY, mapSizeZ)));
        var mapViewState = new MapViewState();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());

        var componentManager = new ComponentManager(100, 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref TransformComponent existing, TransformComponent incoming) => existing = incoming);
        componentManager.RegisterDirectPool<GlyphComponent>(static (ref GlyphComponent existing, GlyphComponent incoming) => existing = incoming);
        componentManager.RegisterDirectPool<BackgroundComponent>(static (ref BackgroundComponent existing, BackgroundComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<OccupancyComponent>(static (ref OccupancyComponent existing, OccupancyComponent incoming) => existing = incoming);

        windowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            fontService, windowService, world, mapViewState, componentManager, new TileRenderer(), new GlyphRenderer()));

        var mapWindow = windowService.CreateWindow<MapWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(1256, 776), DisplayMode = WindowDisplayMode.Fixed },
        });
        mapWindow.Initialize();

        return (world, mapViewState, mapWindow);
    }

    [TestMethod]
    public void SelectMapNodes_ClickWithinViewportButPastMapEdge_DoesNotThrowAndLeavesSelectionUnset()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 1);

        // Team zoom = 12px tiles; the viewport (1256px content / 12 + 1 = 105 columns) is
        // far larger than this 5-wide map, so tile column 10 is visible but off the map.
        mapWindow.SelectMapNodes(new Point(10 * 12 + 1, 1));

        Assert.IsNull(mapViewState.SelectedMapNodePosition);
    }

    [TestMethod]
    public void SelectMapNodes_ClickOnMap_SetsSelection()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 1);

        mapWindow.SelectMapNodes(new Point(1 * 12 + 1, 1 * 12 + 1));

        Assert.AreEqual(new Point(1, 1), mapViewState.SelectedMapNodePosition);
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
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);

        mapWindow.UpdateScrollPosition(new Point(100_000, 0));
        mapWindow.SelectMapNodes(new Point(104 * 12 + 1, 1));

        Assert.AreEqual(new Point(199, 0), mapViewState.SelectedMapNodePosition);
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
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);
        mapWindow.UpdateScrollPosition(new Point(100_000, 0));

        mapWindow.UpdateZoomLevel(ZoomLevel.Borough);
        mapWindow.SelectMapNodes(new Point(1, 1));

        Assert.AreEqual(new Point(0, 0), mapViewState.SelectedMapNodePosition);
    }

    [TestMethod]
    public void ChangeLayer_ClampsToValidRange()
    {
        // 3-deep map (UnderGround/Ground/Flying) -- MapWindow starts on Ground (index 1).
        // CurrentMapLayer lives on MapViewState (shared with SelectionWindowContent), not MapWindow.
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 3);
        Assert.AreEqual(1, mapViewState.CurrentMapLayer);

        mapWindow.ChangeLayer(1);
        Assert.AreEqual(2, mapViewState.CurrentMapLayer);

        mapWindow.ChangeLayer(1);
        Assert.AreEqual(2, mapViewState.CurrentMapLayer, "Already at the topmost layer -- must not go past it.");

        mapWindow.ChangeLayer(-1);
        mapWindow.ChangeLayer(-1);
        Assert.AreEqual(0, mapViewState.CurrentMapLayer);

        mapWindow.ChangeLayer(-1);
        Assert.AreEqual(0, mapViewState.CurrentMapLayer, "Already at the bottommost layer -- must not go below it.");
    }

    /// <summary>
    /// MapWindow's own hotkeys (see OnHotkeysAction) -- GameInputController only ever routes
    /// the whole keyboard state to whichever window is focused (see
    /// GameInputControllerTests.HotkeysAreRoutedToTheFocusedWindow), so these are tested
    /// directly against HandleHotkeys rather than through a real GameInputController.
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_PressingSpace_TogglesIsPaused()
    {
        var (_, _, mapWindow) = BuildMapWindow(5, 5, 1);
        Assert.IsFalse(mapWindow.IsPaused);

        mapWindow.HandleHotkeys(new KeyboardState(Keys.Space), new KeyboardState());
        Assert.IsTrue(mapWindow.IsPaused);

        mapWindow.HandleHotkeys(new KeyboardState(), new KeyboardState(Keys.Space));
        Assert.IsTrue(mapWindow.IsPaused, "Releasing Space must not toggle pause again.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.Space), new KeyboardState());
        Assert.IsFalse(mapWindow.IsPaused);
    }

    [TestMethod]
    public void HandleHotkeys_HoldingD_ScrollsRight()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());

        // Team zoom = 12px tiles; a single frame's scroll change is +1 tile, so clicking at
        // tile-column 0 now resolves to map column 1 instead of 0.
        mapWindow.SelectMapNodes(new Point(1, 1));
        Assert.AreEqual(new Point(1, 0), mapViewState.SelectedMapNodePosition);
    }

    [TestMethod]
    public void HandleHotkeys_PressingPageUp_ChangesLayer()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 3);
        Assert.AreEqual(1, mapViewState.CurrentMapLayer);

        mapWindow.HandleHotkeys(new KeyboardState(Keys.PageUp), new KeyboardState());

        Assert.AreEqual(2, mapViewState.CurrentMapLayer);
    }

    /// <summary>Mirrors UpdateZoomLevel_RecalculatesMaxScrollAndReclampsCurrentPosition above, but via the OemMinus hotkey instead of a direct UpdateZoomLevel call.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingOemMinus_ZoomsOutOneLevelAndRecalculatesMaxScroll()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);
        mapWindow.UpdateScrollPosition(new Point(100_000, 0));

        // OemMinus cycles zoom out one level (Team, 12px tiles -> Neighborhood, 6px tiles);
        // 210 columns are now visible against the 200-wide map, so the previously-valid
        // Team-zoom max scroll (95) must be re-clamped down to 0.
        mapWindow.HandleHotkeys(new KeyboardState(Keys.OemMinus), new KeyboardState());
        mapWindow.SelectMapNodes(new Point(1, 1));

        Assert.AreEqual(new Point(0, 0), mapViewState.SelectedMapNodePosition);
    }
}
