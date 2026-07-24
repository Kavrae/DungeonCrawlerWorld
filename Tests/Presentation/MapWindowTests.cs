using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
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
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);

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
    /// With a 200-wide map and 71 visible columns at Team zoom (1256px content / 18px
    /// tiles + 2 -- see UpdateTileSizes' own margin comment), the correct max scroll is 129,
    /// which puts the map's last column (199) at the window's rightmost visible column
    /// (index 70).
    /// </summary>
    [TestMethod]
    public void UpdateScrollPosition_ScrollingPastMax_StopsWithMapsLastColumnAtWindowsRightEdge()
    {
        var (_, mapViewState, mapWindow) = BuildMapWindow(200, 5, 1);

        mapWindow.UpdateScrollPosition(new Point(100_000, 0));
        mapWindow.SelectMapNodes(new Point(70 * 18 + 1, 1));

        Assert.AreEqual(new Point(199, 0), mapViewState.SelectedMapNodePosition);
    }

    /// <summary>
    /// Regression test: UpdateZoomLevel changed the visible tile count via UpdateTileSizes
    /// but never recalculated max scroll, so it went stale after any zoom change. Scrolling
    /// to Team zoom's max (129, see above), then zooming out to Borough (4px tiles -- the
    /// whole 200-wide map fits in the 1256px content area, so the correct max scroll is 0)
    /// must re-clamp the stale scroll position down to 0, not leave it at 129.
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
    /// The whole point of the smooth-scroll rework: the drag is measured from a fixed start
    /// anchor (not accumulated per frame), and every pixel of movement immediately shifts
    /// rendering (see OnRightDragAction's _renderPixelOffset) -- not just once a whole tile has
    /// accumulated, which is what made panning feel jittery before. A mere 1px drag already
    /// moves the boundary between which screen pixel resolves to which map column.
    /// </summary>
    [TestMethod]
    public void OnRightDragAction_SubTileOffset_ShiftsWhichColumnAClickResolvesTo()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        // Without any drag, a click 1px before column 35's left edge resolves to column 34.
        mapWindow.SelectMapNodes(new Point(35 * 18 - 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(99, 100), mapViewState.SelectedMapNodePosition);

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 1px is nowhere near a whole tile, but must already shift
        // rendering by exactly that much.
        mapWindow.HandleRightDrag(new Vector2(-1, 0));

        mapWindow.SelectMapNodes(new Point(35 * 18 - 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "A 1px drag must already shift rendering by 1px, not wait for a whole tile to accumulate.");
    }

    /// <summary>
    /// The underlying tile grid (_currentScrollPosition/the background cache) only ever
    /// commits whole-tile steps -- a drag under a full tile leaves it untouched; only the
    /// render-time offset moves. The grid isn't "snapped to" (settled with zero offset) until
    /// the drag ends -- see OnRightDragEndAction's own tests below.
    /// </summary>
    [TestMethod]
    public void OnRightDragAction_MidDrag_DoesNotCommitUntilAFullTileIsCrossed()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 10px is comfortably short of a whole tile (unlike a value
        // near 18, this can't also tip the click's own resolved column over a boundary).
        mapWindow.HandleRightDrag(new Vector2(-10, 0));

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "A drag under a full tile must not commit a grid scroll while still in progress.");
    }

    /// <summary>
    /// Regression test: _tileColumns/_tileRows originally had only a single extra tile of
    /// margin beyond the minimum needed to cover the content area (enough for a partial tile
    /// sitting at the edge when _renderPixelOffset is always 0). Once dragging could shift
    /// rendering by up to a whole tile, that single tile of margin ran out partway through a
    /// drag, leaving the right/bottom edge with no tile rendered there at all until the next
    /// whole-tile commit -- visible as that edge's tiles flickering in and out (see
    /// UpdateTileSizes' own margin comment for why a second extra tile fixes this). A click
    /// right at the window's actual content edge, during a near-full-tile drag, is exactly
    /// the scenario that starves without that second tile of margin.
    /// </summary>
    [TestMethod]
    public void OnRightDragAction_NearFullTileDrag_StillResolvesAClickAtTheContentsFarEdge()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 17px is as close to a full tile as possible without
        // crossing it -- the worst case for how far the render offset can eat into the margin.
        mapWindow.HandleRightDrag(new Vector2(-17, 0));

        // 2px inside the window's actual content edge (1256px) -- must still resolve to a
        // real, on-map position, not be silently rejected for landing past _tileColumns.
        mapWindow.SelectMapNodes(new Point(1254, 22 * 18 + 1));
        Assert.IsNotNull(mapViewState.SelectedMapNodePosition, "The window's far edge must still resolve correctly during a near-full-tile drag.");
    }

    /// <summary>Ending the drag settles whatever sub-tile remainder is left onto the nearest whole tile -- past the halfway point rounds up to the next one.</summary>
    [TestMethod]
    public void OnRightDragEndAction_SnapsRemainderPastHalfATile_ToTheNextTile()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 10px is just past half a tile (9px).
        mapWindow.HandleRightDrag(new Vector2(-10, 0));
        mapWindow.HandleRightDragEnd();

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(101, 100), mapViewState.SelectedMapNodePosition, "Ending the drag must snap a past-half-tile remainder up to the next tile.");
    }

    /// <summary>The other side of the rounding threshold -- an under-half-tile remainder settles back onto the current tile, not the next one.</summary>
    [TestMethod]
    public void OnRightDragEndAction_SnapsRemainderUnderHalfATile_BackToTheCurrentTile()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightDragStart();

        // Team zoom = 18px tiles; 8px is just under half a tile (9px).
        mapWindow.HandleRightDrag(new Vector2(-8, 0));
        mapWindow.HandleRightDragEnd();

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "Ending the drag must settle an under-half-tile remainder back onto the current tile, not advance it.");
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

    /// <summary>
    /// Page Up/Down can leave the viewed layer arbitrarily far from whatever layer the player
    /// actually occupies, with no way back except manually paging back -- HOME (and initial
    /// startup) must switch the viewed layer back to the player's own, not just recenter X/Y.
    /// </summary>
    [TestMethod]
    public void Initialize_AndHandleHotkeys_PressingHome_SyncViewedLayerToThePlayers()
    {
        var (_, mapViewState, mapWindow, _) = BuildMapWindowWithPlayer(300, 300, 3, new Vector3Int(100, 100, 0));

        Assert.AreEqual(0, mapViewState.CurrentMapLayer, "Should start viewing the player's own layer (UnderGround), not the default Ground.");

        mapWindow.ChangeLayer(2);
        Assert.AreEqual(2, mapViewState.CurrentMapLayer, "Sanity check -- now viewing Flying, away from the player.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.Home), new KeyboardState());

        Assert.AreEqual(0, mapViewState.CurrentMapLayer, "HOME must switch back to the layer the player actually occupies.");
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
        // 100 wide: bigger than Team's 71-column viewport (so there's a real max scroll to
        // start from) but small enough to fully fit Neighborhood's 141-column viewport (so
        // the re-clamp actually lands on 0, not some other nonzero bound).
        var (_, mapViewState, mapWindow) = BuildMapWindow(100, 5, 1);
        mapWindow.UpdateScrollPosition(new Point(100_000, 0));

        // OemMinus cycles zoom out one level (Team, 18px tiles -> Neighborhood, 9px tiles);
        // 141 columns are now visible against the 100-wide map, so the previously-valid
        // Team-zoom max scroll (29) must be re-clamped down to 0.
        mapWindow.HandleHotkeys(new KeyboardState(Keys.OemMinus), new KeyboardState());
        mapWindow.SelectMapNodes(new Point(1, 1));

        Assert.AreEqual(new Point(0, 0), mapViewState.SelectedMapNodePosition);
    }
}