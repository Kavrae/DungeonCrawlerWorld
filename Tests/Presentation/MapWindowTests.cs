using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
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
    private const int PlayerEntityId = 1;

    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow) BuildMapWindow(int mapSizeX, int mapSizeY, int mapSizeZ)
    {
        var (world, mapViewState, mapWindow, _) = BuildMapWindowCore(mapSizeX, mapSizeY, mapSizeZ, playerPosition: null);
        return (world, mapViewState, mapWindow);
    }

    /// <summary>Same as BuildMapWindow, plus a MovementMode.PlayerControlled player entity at playerPosition -- for exercising WASD movement/camera-follow, which need World.PlayerEntityId wired to something real.</summary>
    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow, ComponentManager ComponentManager) BuildMapWindowWithPlayer(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int playerPosition) =>
        BuildMapWindowCore(mapSizeX, mapSizeY, mapSizeZ, playerPosition);

    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow, ComponentManager ComponentManager) BuildMapWindowCore(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int? playerPosition)
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(mapSizeX, mapSizeY, mapSizeZ)));
        var mapViewState = new MapViewState();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());

        var componentManager = new ComponentManager(100, 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<GlyphComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<BackgroundComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<OccupancyComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) => existing = incoming);

        if (playerPosition is { } position)
        {
            componentManager.Merge(PlayerEntityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, 0, null, null));
            world.PlayerEntityId = PlayerEntityId;
        }

        windowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            fontService, windowService, world, mapViewState, componentManager, new TileRenderer(), new GlyphRenderer()));

        var mapWindow = windowService.CreateWindow<MapWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(1256, 776), DisplayMode = WindowDisplayMode.Fixed },
        });
        mapWindow.Initialize();

        return (world, mapViewState, mapWindow, componentManager);
    }

    [TestMethod]
    public void SelectMapNodes_ClickWithinViewportButPastMapEdge_DoesNotThrowAndLeavesSelectionUnset()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 1);

        // Team zoom = 18px tiles; the viewport (1256px content / 18 + 1 = 70 columns) is
        // far larger than this 5-wide map, so tile column 10 is visible but off the map.
        mapWindow.SelectMapNodes(new Point(10 * 18 + 1, 1));

        Assert.IsNull(mapViewState.SelectedMapNodePosition);
    }

    [TestMethod]
    public void SelectMapNodes_ClickOnMap_SetsSelection()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(5, 5, 1);

        mapWindow.SelectMapNodes(new Point(1 * 18 + 1, 1 * 18 + 1));

        Assert.AreEqual(new Point(1, 1), mapViewState.SelectedMapNodePosition);
    }

    /// <summary>
    /// Regression test: Initialize called UpdateMaxScrollPosition before UpdateTileSizes, so
    /// max scroll was computed against a still-zero visible tile count -- the bound ended up
    /// as the full map width/height instead of (map size - visible tiles), letting the map
    /// scroll a whole extra viewport past its real edge (reported as "scrolls too far").
    /// With a 200-wide map and 70 visible columns at Team zoom (1256px content / 18px
    /// tiles + 1), the correct max scroll is 130, which puts the map's last column (199) at
    /// the window's rightmost visible column (index 69).
    /// </summary>
    [TestMethod]
    public void UpdateScrollPosition_ScrollingPastMax_StopsWithMapsLastColumnAtWindowsRightEdge()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);

        mapWindow.UpdateScrollPosition(new Point(100_000, 0));
        mapWindow.SelectMapNodes(new Point(69 * 18 + 1, 1));

        Assert.AreEqual(new Point(199, 0), mapViewState.SelectedMapNodePosition);
    }

    /// <summary>
    /// Regression test: UpdateZoomLevel changed the visible tile count via UpdateTileSizes
    /// but never recalculated max scroll, so it went stale after any zoom change. Scrolling
    /// to Team zoom's max (130, see above), then zooming out to Borough (4px tiles -- the
    /// whole 200-wide map fits in the 1256px content area, so the correct max scroll is 0)
    /// must re-clamp the stale scroll position down to 0, not leave it at 130.
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

    /// <summary>
    /// WASD moves the player character (through MovementComponent.NextMapPosition, like any
    /// other entity -- see MapWindow.TryQueuePlayerMove), not the camera. A fresh press moves
    /// immediately (no initial delay), and the camera automatically recenters on the queued
    /// move's target (not a stale re-read of TransformComponent, since MovementSystem hasn't
    /// actually applied the move yet at input time).
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_PressingD_MovesPlayerImmediatelyAndCameraFollows()
    {
        var (_, mapViewState, mapWindow, componentManager) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));
        var movementPool = componentManager.GetPackedPool<MovementComponent>();

        // Team zoom = 18px tiles, viewport is 70 columns x 44 rows; column/row 35/22 is
        // screen-center, so clicking there resolves to the player's own position once the
        // camera starts centered on them (see MapWindow.Initialize's initial CenterCameraOn).
        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "Camera should start centered on the player.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());
        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "A fresh press must move immediately, not wait out an initial cooldown.");

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(101, 100), mapViewState.SelectedMapNodePosition, "Camera should follow the queued move's target position.");
    }

    /// <summary>
    /// The cooldown between repeats is a single counter shared across all four directions and
    /// ticks down regardless of what's held or released -- so it can't be reset early by
    /// releasing, switching direction, or rapidly alternating keys, which would otherwise let
    /// a player move every frame by just tapping a different key each time.
    /// </summary>
    [TestMethod]
    public void HandlePlayerMovementInput_AlternatingDirectionsDuringCooldown_DoesNotBypassCooldown()
    {
        var (_, _, mapWindow, componentManager) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));
        var movementPool = componentManager.GetPackedPool<MovementComponent>();

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());
        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition);

        // None of these should queue a new move -- the shared cooldown is still active.
        mapWindow.HandleHotkeys(new KeyboardState(Keys.W), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.A), new KeyboardState(Keys.W));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.S), new KeyboardState(Keys.A));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState(Keys.S));

        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "Alternating directions must not bypass the shared cooldown.");
    }

    /// <summary>
    /// Once the cooldown elapses (and the player is at rest again -- simulated here since no
    /// MovementSystem runs in this MapWindow-level test), holding the same direction repeats
    /// exactly every FramesPerPlayerMove (15) frames, not sooner.
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_HoldingD_RepeatsEveryFramesPerPlayerMoveFrames()
    {
        var (_, _, mapWindow, componentManager) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));
        var movementPool = componentManager.GetPackedPool<MovementComponent>();
        var transformPool = componentManager.GetDirectPool<TransformComponent>();

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());
        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition);

        // Simulate MovementSystem having applied the first move, so the player reads as "at
        // rest" again and a repeat can be considered.
        transformPool.Get(PlayerEntityId).Position = new Vector3Int(101, 100, 0);

        for (var frame = 0; frame < 14; frame++)
        {
            mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState(Keys.D));
        }
        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "Must not repeat before FramesPerPlayerMove has elapsed.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState(Keys.D));
        Assert.AreEqual(new Vector3Int(102, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "Must repeat once FramesPerPlayerMove has elapsed since the last move.");
    }

    /// <summary>MovementSystem's TryMoveToNextMapPosition never re-validates bounds/occupancy for MovementMode.PlayerControlled -- TryQueuePlayerMove must reject an off-map candidate itself before ever writing NextMapPosition.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingA_AtMapEdge_DoesNotQueueAnOffMapMove()
    {
        var (_, _, mapWindow, componentManager) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(0, 100, 0));
        var movementPool = componentManager.GetPackedPool<MovementComponent>();

        mapWindow.HandleHotkeys(new KeyboardState(Keys.A), new KeyboardState());

        Assert.IsNull(movementPool.GetReadonly(PlayerEntityId).NextMapPosition);
    }

    /// <summary>
    /// A right-mouse-drag pans the camera directly (not through the player) and decouples it
    /// from following the player until HOME re-centers/re-couples.
    /// </summary>
    [TestMethod]
    public void RightMouseDrag_DecouplesCameraFromPlayer_UntilHomeRecouples()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        // Team zoom = 18px tiles; drag left by 3 tiles' worth of pixels.
        mapWindow.HandleRightDragStart();
        mapWindow.HandleRightDrag(new Vector2(-54, 0));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreNotEqual(new Point(101, 100), mapViewState.SelectedMapNodePosition, "Camera must not follow the player once right-drag has decoupled it.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.Home), new KeyboardState());
        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));

        // Centers on the player's actual TransformComponent.Position (100,100) -- there's no
        // MovementSystem running in this MapWindow-level test to ever apply the queued
        // NextMapPosition (101,100), which is exactly why HOME must read the real transform
        // rather than assuming whatever was last queued.
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "HOME must re-center on the player's actual position.");
    }

    /// <summary>
    /// The drag is measured from a fixed start anchor, not accumulated per frame (see
    /// OnRightDragAction), and rounds to the nearest tile -- so a drag just past half a tile
    /// already registers, rather than requiring a nearly-full tile of movement first.
    /// </summary>
    [TestMethod]
    public void OnRightDragAction_PastHalfATile_RoundsUpToOneTileOfScroll()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 10px is just past half a tile (9px).
        mapWindow.HandleRightDrag(new Vector2(-10, 0));

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(101, 100), mapViewState.SelectedMapNodePosition);
    }

    /// <summary>The other side of the rounding threshold -- under half a tile of drag must not scroll at all yet.</summary>
    [TestMethod]
    public void OnRightDragAction_LessThanHalfATile_DoesNotScrollYet()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 8px is just under half a tile (9px).
        mapWindow.HandleRightDrag(new Vector2(-8, 0));

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "A drag under half a tile must not yet produce any scroll.");
    }

    /// <summary>HOME must center on the player, but never scroll past the map's borders even when that means the player isn't exactly centered.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingHome_ClampsToMapBounds()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(2, 2, 0));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.Home), new KeyboardState());

        mapWindow.SelectMapNodes(new Point(1, 1));
        Assert.AreEqual(new Point(0, 0), mapViewState.SelectedMapNodePosition, "Centering on (2,2) would want a negative scroll -- must clamp to 0, not go out of bounds.");
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
        // 100 wide: bigger than Team's 70-column viewport (so there's a real max scroll to
        // start from) but small enough to fully fit Neighborhood's 140-column viewport (so
        // the re-clamp actually lands on 0, not some other nonzero bound).
        var (_, mapViewState, mapWindow) = BuildMapWindow(100, 5, 1);
        mapWindow.UpdateScrollPosition(new Point(100_000, 0));

        // OemMinus cycles zoom out one level (Team, 18px tiles -> Neighborhood, 9px tiles);
        // 140 columns are now visible against the 100-wide map, so the previously-valid
        // Team-zoom max scroll (30) must be re-clamped down to 0.
        mapWindow.HandleHotkeys(new KeyboardState(Keys.OemMinus), new KeyboardState());
        mapWindow.SelectMapNodes(new Point(1, 1));

        Assert.AreEqual(new Point(0, 0), mapViewState.SelectedMapNodePosition);
    }
}