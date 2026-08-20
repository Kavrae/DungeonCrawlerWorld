using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatusEffectAura.Components;
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

    /// <summary>Same as BuildMapWindowWithPlayer, but also hands back the ActionCatalog MapWindow was built with -- for hotkey/action tests that need to register a test ActionDefinition before pressing anything.</summary>
    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow, ComponentManager ComponentManager, ActionCatalog ActionCatalog) BuildMapWindowWithPlayerAndActions(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int playerPosition)
    {
        var actionCatalog = new ActionCatalog();
        var (world, mapViewState, mapWindow, componentManager) = BuildMapWindowCore(mapSizeX, mapSizeY, mapSizeZ, playerPosition, actionCatalog);
        return (world, mapViewState, mapWindow, componentManager, actionCatalog);
    }

    /// <summary>Same as BuildMapWindowWithPlayerAndActions, but hands back the ItemCatalog instead -- for hotkey/item tests that need to register a test ItemDefinition before pressing anything.</summary>
    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow, ComponentManager ComponentManager, ItemCatalog ItemCatalog) BuildMapWindowWithPlayerAndItems(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int playerPosition)
    {
        var itemCatalog = new ItemCatalog();
        var (world, mapViewState, mapWindow, componentManager) = BuildMapWindowCore(mapSizeX, mapSizeY, mapSizeZ, playerPosition, itemCatalog: itemCatalog);
        return (world, mapViewState, mapWindow, componentManager, itemCatalog);
    }

    private static (Game.World.World World, MapViewState MapViewState, MapWindow MapWindow, ComponentManager ComponentManager) BuildMapWindowCore(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int? playerPosition, ActionCatalog? actionCatalog = null, ItemCatalog? itemCatalog = null)
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(mapSizeX, mapSizeY, mapSizeZ)));
        var mapViewState = new MapViewState();
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());

        var componentManager = new ComponentManager(100, 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<GlyphComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<SpriteComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<BackgroundComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<NonBlockingComponent>();
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectAuraSourceComponent>();
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);

        if (playerPosition is { } position)
        {
            componentManager.Merge(PlayerEntityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
            // Fully unlocked -- these tests are about arm/target/confirm behavior, not the Expansion lock itself, so default to every slot being usable rather than incidentally locking out whichever slot a given test happens to bind to.
            componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 20));
            world.PlayerEntityId = PlayerEntityId;
        }

        var resolvedActionCatalog = actionCatalog ?? new ActionCatalog();
        var resolvedItemCatalog = itemCatalog ?? new ItemCatalog();
        var camera = new MapCamera(world);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            resolvedActionCatalog,
            resolvedItemCatalog,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetMultiPool<ActionHotkeyBindingComponent>(),
            componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>());
        var playerMovement = new PlayerMovementController(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>());

        windowService.RegisterFactory<MapWindow>(() => new MapWindow(
            fontService, windowService, world, mapViewState, componentManager, new EventBus(), resolvedActionCatalog, resolvedItemCatalog, new TileRenderer(), new GlyphRenderer(),
            new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), camera, actionTargeting, playerMovement));

        var mapWindow = windowService.CreateElement<MapWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(1256, 776), DisplayMode = ElementDisplayMode.Fixed },
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
    /// MapWindow's own hotkeys (see OnHotkeysAction) -- UiInputController only ever routes
    /// the whole keyboard state to whichever window is focused (see
    /// UiInputControllerTests.HotkeysAreRoutedToTheFocusedWindow), so these are tested
    /// directly against HandleHotkeys rather than through a real UiInputController.
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
    /// immediately (no initial delay), but the camera must not recenter until the queued move
    /// actually lands -- MovementSystem applies it later (possibly much later, if the player's
    /// action lock is still counting down from a previous move), and snapping the camera ahead
    /// of the entity used to make the camera visibly jump before the glyph caught up to it.
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_PressingD_MovesPlayerImmediatelyButCameraWaitsForTheActualMove()
    {
        var (_, mapViewState, mapWindow, componentManager) = BuildMapWindowWithPlayer(300, 300, 1, new Vector3Int(100, 100, 0));
        var movementPool = componentManager.GetPackedPool<MovementComponent>();
        var transformPool = componentManager.GetDirectPool<TransformComponent>();

        // Team zoom = 18px tiles, viewport is 70 columns x 44 rows; column/row 35/22 is
        // screen-center, so clicking there resolves to the player's own position once the
        // camera starts centered on them (see MapWindow.Initialize's initial CenterCameraOn).
        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "Camera should start centered on the player.");

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D), new KeyboardState());
        Assert.AreEqual(new Vector3Int(101, 100, 0), movementPool.GetReadonly(PlayerEntityId).NextMapPosition, "A fresh press must move immediately, not wait out an initial cooldown.");

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(100, 100), mapViewState.SelectedMapNodePosition, "Camera must not follow a merely-queued target -- MovementSystem hasn't moved the entity yet.");

        // Simulate MovementSystem actually applying the move (e.g. once the action lock clears).
        transformPool.TryUpdate(PlayerEntityId, static (ref TransformComponent transform) => transform.Position = new Vector3Int(101, 100, 0));
        mapWindow.Update(new GameTime());

        mapWindow.SelectMapNodes(new Point(35 * 18 + 1, 22 * 18 + 1));
        Assert.AreEqual(new Point(101, 100), mapViewState.SelectedMapNodePosition, "Camera should follow once the entity's own position actually changes.");
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

    private static readonly Guid TestActionId = new("99999999-9999-9999-9999-999999999999");

    private static void RegisterTestAdjacentAction(ActionCatalog actionCatalog) =>
        actionCatalog.Register(new ActionDefinition(
            TestActionId, "Test Adjacent", null, "#", default, [],
            Effects: [ActionEffect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Adjacent, Range: 0), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));

    [TestMethod]
    public void HandleHotkeys_PressingBoundSlot_ArmsIt()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        Assert.AreEqual(TestActionId, mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot4, mapViewState.ArmedSlot);
    }

    /// <summary>A non-double-tap re-press confirms against wherever the cursor currently is -- same as a click -- rather than cancelling (see HandleActionSlotPress's own doc comment; right-click/Escape is the cancel path now).</summary>
    [TestMethod]
    public void HandleHotkeys_PressingArmedSlotAgainAfterDoubleTapWindowElapses_ConfirmsAgainstHoveredTile()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        Assert.IsNotNull(mapViewState.ArmedActionId);

        // Advance well past the ~18-frame double-tap window so the next press reads as an
        // independent press, not a double-tap.
        for (var i = 0; i < 20; i++)
        {
            mapWindow.Update(new GameTime());
        }

        // One of the caster's 8 neighbors -- part of Adjacent's fixed footprint (the caster's own tile no longer is).
        mapWindow.UpdateHoveredTile(ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(101, 100, 0)));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        Assert.AreEqual(TestActionId, pendingActivations.GetReadonly(PlayerEntityId).ActionId);
        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    /// <summary>With no hovered tile at all (cursor never over the map), a re-press has nothing to confirm against -- a miss, same as HandleClick_ArmedAction_ClickOutsideTargetableTiles_DoesNothingAndStaysArmed -- so it stays armed rather than silently cancelling.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingArmedSlotAgainWithNoHoveredTile_DoesNothingAndStaysArmed()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        Assert.IsNotNull(mapViewState.ArmedActionId);

        for (var i = 0; i < 20; i++)
        {
            mapWindow.Update(new GameTime());
        }

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId));
        Assert.IsNotNull(mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot4, mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void HandleHotkeys_PressingUnboundSlot_DoesNothing()
    {
        var (_, mapViewState, mapWindow, _, _) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    /// <summary>
    /// Two presses with no Update call in between leave the frame counter unchanged, so the
    /// second press reads as a double-tap (see HandleHotkeySlotPress) -- this activates
    /// immediately against Adjacent's fixed footprint (the caster's 8 surrounding neighbors,
    /// excluding the caster's own tile) rather than merely arming, and the queued
    /// PendingActionActivationComponent is the observable proof (actual damage application is
    /// ActionActivationSystem's own responsibility, covered by ActionActivationSystemTests, not
    /// exercised by this ComponentManager-only test harness).
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_DoubleTapAdjacentAction_QueuesActivationAgainstTheFixedFootprint_AndClearsAnyArming()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(TestActionId, pending.ActionId);
        Assert.HasCount(8, pending.TargetTiles);
        CollectionAssert.DoesNotContain(pending.TargetTiles, new Vector3Int(100, 100, 0));
        CollectionAssert.Contains(pending.TargetTiles, new Vector3Int(101, 100, 0));

        Assert.IsNull(mapViewState.ArmedActionId, "The first press of the pair armed this slot -- once the double-tap fires, it shouldn't be left stale-armed.");
    }

    /// <summary>SingleTarget abilities have no fixed footprint -- double-tap must pick an actual occupied tile within range via ClosestPointSelector, not just fire at the caster's own position.</summary>
    [TestMethod]
    public void HandleHotkeys_DoubleTapSingleTargetAction_AutoTargetsTheOccupiedTileInRange()
    {
        const int TargetEntityId = 2;
        var (world, _, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var rangedActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(
            rangedActionId, "Test Ranged", null, "*", default, [],
            Effects: [ActionEffect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(rangedActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, rangedActionId));

        var targetPosition = new Vector3Int(105, 100, 0);
        var targetTransform = new TransformComponent(targetPosition, new Vector2Byte(1, 1));
        componentManager.Merge(TargetEntityId, targetTransform);
        world.PlaceEntityOnMap(TargetEntityId, targetPosition, ref targetTransform);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 100));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(rangedActionId, pending.ActionId);
        Assert.HasCount(1, pending.TargetTiles);
        Assert.AreEqual(targetPosition, pending.TargetTiles[0]);
    }

    [TestMethod]
    public void HandleHotkeys_DoubleTapWithNoOccupiedTileInRange_QueuesNoActivation()
    {
        var (_, _, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var rangedActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(
            rangedActionId, "Test Ranged", null, "*", default, [],
            Effects: [ActionEffect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(rangedActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, rangedActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());

        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId));
    }

    /// <summary>
    /// Screen position that resolves to targetMapPosition's X/Y. Walks outward from an anchor
    /// pixel one screen pixel at a time per axis, using SelectMapNodes/SelectedMapNodePosition
    /// (the exact same mouse-to-tile math UpdateHoveredTile itself uses) to check when the
    /// resolved tile matches -- deliberately not a pixels-per-tile ratio computed from a single
    /// probe pair, since a probe delta that isn't an exact multiple of the (unknown, not part of
    /// this class's public surface) tile size silently rounds to the wrong ratio. A pixel-walk
    /// has no such rounding error, at the cost of needing several SelectMapNodes calls instead
    /// of one. Mutates mapViewState.SelectedMapNodePosition as a side effect; harmless for hover
    /// tests, which don't assert on it.
    /// </summary>
    private static Point ComputeScreenPositionForMapPosition(MapWindow mapWindow, MapViewState mapViewState, Vector3Int targetMapPosition)
    {
        // Bounds the walk to comfortably more pixels than any window under test is wide/tall --
        // a target tile outside the visible viewport can never be reached this way (SelectMapNodes
        // silently leaves SelectedMapNodePosition stale once the walk exits the viewport, which
        // would otherwise spin the loop below forever instead of failing loudly).
        const int maxSteps = 4000;

        var screenX = 500;
        var screenY = 500;

        mapWindow.SelectMapNodes(new Point(screenX, screenY));
        var current = mapViewState.SelectedMapNodePosition!.Value;

        var steps = 0;
        while (current.X != targetMapPosition.X)
        {
            Assert.IsLessThan(maxSteps, ++steps, "targetMapPosition is likely outside the visible viewport -- pick a tile the map window can actually resolve a click against.");
            screenX += current.X < targetMapPosition.X ? 1 : -1;
            mapWindow.SelectMapNodes(new Point(screenX, screenY));
            current = mapViewState.SelectedMapNodePosition!.Value;
        }

        steps = 0;
        while (current.Y != targetMapPosition.Y)
        {
            Assert.IsLessThan(maxSteps, ++steps, "targetMapPosition is likely outside the visible viewport -- pick a tile the map window can actually resolve a click against.");
            screenY += current.Y < targetMapPosition.Y ? 1 : -1;
            mapWindow.SelectMapNodes(new Point(screenX, screenY));
            current = mapViewState.SelectedMapNodePosition!.Value;
        }

        return new Point(screenX, screenY);
    }

    [TestMethod]
    public void HandleHotkeys_ArmingAdjacentAction_SetsTargetableTilesToTheFixedFootprint()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        Assert.IsNotNull(mapViewState.TargetableTiles);
        Assert.HasCount(8, mapViewState.TargetableTiles);
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(100, 100, 0)), "The caster's own tile is no longer part of Adjacent's footprint.");
        Assert.IsTrue(mapViewState.TargetableTiles.Contains(new Vector3Int(101, 100, 0)));
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(102, 100, 0)));
    }

    [TestMethod]
    public void HandleHotkeys_ArmingRangedAction_SetsTargetableTilesToTheFullDiamondWithinRange()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var rangedActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(rangedActionId, "Test Ranged", null, "*", default, [], Effects: [ActionEffect.None], Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(rangedActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, rangedActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());

        // Diamond of radius 10: 2*10^2 + 2*10 + 1 = 221 (same worked formula as DistanceFalloffTests).
        Assert.IsNotNull(mapViewState.TargetableTiles);
        Assert.HasCount(221, mapViewState.TargetableTiles);
        Assert.IsTrue(mapViewState.TargetableTiles.Contains(new Vector3Int(110, 100, 0)), "Exactly at range 10 must be included.");
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(111, 100, 0)), "Beyond range 10 must be excluded.");
    }

    /// <summary>
    /// TargetableTiles must follow the caster, not stay anchored to wherever it was standing at
    /// arm time -- Update (via ActionTargetingController.RefreshTargetableTiles) recomputes it
    /// from the caster's current TransformComponent every frame an ability stays armed.
    /// </summary>
    [TestMethod]
    public void HandleHotkeys_CasterMovesWhileArmed_RecomputesTargetableTilesFromTheNewPosition()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        var transformPool = componentManager.GetDirectPool<TransformComponent>();

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        Assert.IsNotNull(mapViewState.TargetableTiles);
        Assert.IsTrue(mapViewState.TargetableTiles.Contains(new Vector3Int(101, 100, 0)));
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(105, 100, 0)));

        // Simulate MovementSystem actually applying a move while the ability stays armed.
        transformPool.TryUpdate(PlayerEntityId, static (ref TransformComponent transform) => transform.Position = new Vector3Int(105, 100, 0));
        mapWindow.Update(new GameTime());

        Assert.IsNotNull(mapViewState.TargetableTiles);
        Assert.HasCount(8, mapViewState.TargetableTiles);
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(101, 100, 0)), "The footprint must move with the caster, not stay anchored to the position it was armed at.");
        Assert.IsFalse(mapViewState.TargetableTiles.Contains(new Vector3Int(105, 100, 0)), "The caster's own new tile is no longer part of Adjacent's footprint either.");
        Assert.IsTrue(mapViewState.TargetableTiles.Contains(new Vector3Int(106, 100, 0)));
    }

    /// <summary>Right-click/Escape is the cancel path now (see CancelArmedOrPendingAction) -- re-pressing the same hotkey confirms rather than disarms, so this exercises the actual disarm path.</summary>
    [TestMethod]
    public void HandleRightClickTap_Disarming_ClearsTargetableTiles()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        Assert.IsNotNull(mapViewState.TargetableTiles);

        mapWindow.HandleRightClickTap();

        Assert.IsNull(mapViewState.TargetableTiles);
    }

    [TestMethod]
    public void UpdateHoveredTile_NothingArmed_HoveredTileAndFootprintStayEmpty()
    {
        var (_, mapViewState, mapWindow, _, _) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.UpdateHoveredTile(ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(101, 100, 0)));
        mapViewState.SelectedMapNodePosition = null; // Undo the calibration probes' side effect -- this test doesn't concern SelectedMapNodePosition.

        Assert.IsNull(mapViewState.HoveredTile);
        Assert.IsEmpty(mapWindow.HoveredFootprint.ToList());
    }

    [TestMethod]
    public void UpdateHoveredTile_ArmedSingleTargetAction_HoveringWithinRange_SetsHoveredTileAndFootprint()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var rangedActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(rangedActionId, "Test Ranged", null, "*", default, [], Effects: [ActionEffect.None], Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(rangedActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, rangedActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());
        mapWindow.UpdateHoveredTile(ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(101, 100, 0)));

        Assert.AreEqual(new Vector3Int(101, 100, 0), mapViewState.HoveredTile);
        Assert.HasCount(1, mapWindow.HoveredFootprint);
        Assert.AreEqual(new Vector3Int(101, 100, 0), mapWindow.HoveredFootprint[0]);
    }

    [TestMethod]
    public void UpdateHoveredTile_ArmedSingleTargetAction_HoveringBeyondRange_SetsHoveredTile_ButFootprintStaysEmpty()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var rangedActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(rangedActionId, "Test Ranged", null, "*", default, [], Effects: [ActionEffect.None], Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(rangedActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, rangedActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());
        mapWindow.UpdateHoveredTile(ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(115, 100, 0)));

        Assert.AreEqual(new Vector3Int(115, 100, 0), mapViewState.HoveredTile);
        Assert.IsEmpty(mapWindow.HoveredFootprint.ToList());
    }

    [TestMethod]
    public void UpdateHoveredTile_MouseOffMap_SetsHoveredTileNullEvenWhileArmed()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        mapWindow.UpdateHoveredTile(new Point(-100, -100));

        Assert.IsNull(mapViewState.HoveredTile);
        Assert.IsEmpty(mapWindow.HoveredFootprint.ToList());
    }

    [TestMethod]
    public void HandleClick_ArmedAdjacentAction_ClickWithinFootprint_QueuesActivationAndDisarms()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        // One of the caster's 8 neighbors -- part of Adjacent's fixed footprint (the caster's own tile no longer is).
        var clickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(101, 100, 0));
        mapWindow.HandleClick(clickPosition);

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(TestActionId, pending.ActionId);
        Assert.HasCount(8, pending.TargetTiles);
        Assert.IsNull(mapViewState.ArmedActionId, "Confirming a target must disarm.");
        Assert.IsNull(mapViewState.TargetableTiles);
    }

    [TestMethod]
    public void HandleClick_ArmedAction_ClickOutsideTargetableTiles_DoesNothingAndStaysArmed()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        // Outside Adjacent's 9-tile footprint around (100,100), but still comfortably within the visible viewport.
        var farClickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(105, 100, 0));
        mapWindow.HandleClick(farClickPosition);

        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(PlayerEntityId));
        Assert.IsNotNull(mapViewState.ArmedActionId, "A miss shouldn't disarm -- the player can just try again.");
    }

    [TestMethod]
    public void HandleClick_ArmedLineAction_ClickInDirection_QueuesActivationAlongThatLine()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var lineActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(lineActionId, "Test Line", null, "#", default, [], Effects: [ActionEffect.None], Activator: new DirectAction(new TargetingSpec(TargetShape.Line, Range: 2), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(lineActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, lineActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        // Two tiles east -- within the range-2 candidate diamond ComputeTargetableTiles builds around the caster.
        var clickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(102, 100, 0));
        mapWindow.HandleClick(clickPosition);

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(lineActionId, pending.ActionId);
        CollectionAssert.AreEqual(new[] { new Vector3Int(101, 100, 0), new Vector3Int(102, 100, 0) }, pending.TargetTiles);
    }

    [TestMethod]
    public void HandleClick_ArmedBurstAction_ClickAwayFromCaster_QueuesFootprintCenteredOnClickedTile()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        var burstActionId = Guid.NewGuid();
        actionCatalog.Register(new ActionDefinition(burstActionId, "Test Burst", null, "*", default, [], Effects: [ActionEffect.None], Activator: new DirectAction(new TargetingSpec(TargetShape.Burst, Range: 10, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(burstActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot5, burstActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D5), new KeyboardState());

        var clickedTile = new Vector3Int(105, 100, 0);
        var clickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, clickedTile);
        mapWindow.HandleClick(clickPosition);

        var pendingActivations = componentManager.GetPackedPool<PendingActionActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(burstActionId, pending.ActionId);
        Assert.HasCount(5, pending.TargetTiles, "areaSize 1 -> radius-1 diamond centered on the clicked tile, not the caster.");
        CollectionAssert.Contains(pending.TargetTiles, clickedTile);
        CollectionAssert.DoesNotContain(pending.TargetTiles, new Vector3Int(100, 100, 0));
    }

    [TestMethod]
    public void HandleRightClickTap_ArmedAction_CancelsIt()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        mapWindow.HandleRightClickTap();

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedSlot);
        Assert.IsNull(mapViewState.TargetableTiles);
    }

    [TestMethod]
    public void HandleEscape_ArmedAction_CancelsIt()
    {
        var (_, mapViewState, mapWindow, componentManager, actionCatalog) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestAdjacentAction(actionCatalog);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        mapWindow.HandleEscape();

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.IsNull(mapViewState.ArmedSlot);
        Assert.IsNull(mapViewState.TargetableTiles);
    }

    [TestMethod]
    public void HandleRightClickTap_NothingArmed_PendingDelayedAction_CancelsItAndZeroesTheActionLock()
    {
        var (_, _, mapWindow, componentManager, _) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        componentManager.Merge(PlayerEntityId, new PendingDelayedActionComponent(Guid.NewGuid(), [new Vector3Int(101, 100, 0)]));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 60, currentLockFramesRemaining: 45));

        mapWindow.HandleRightClickTap();

        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(PlayerEntityId));
        Assert.AreEqual((ushort?)0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(PlayerEntityId).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void HandleEscape_NothingArmed_PendingDelayedAction_CancelsItAndZeroesTheActionLock()
    {
        var (_, _, mapWindow, componentManager, _) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));
        componentManager.Merge(PlayerEntityId, new PendingDelayedActionComponent(Guid.NewGuid(), [new Vector3Int(101, 100, 0)]));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 60, currentLockFramesRemaining: 45));

        mapWindow.HandleEscape();

        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(PlayerEntityId));
        Assert.AreEqual((ushort?)0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(PlayerEntityId).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void HandleRightClickTapAndEscape_NothingArmedOrPending_DoNothing()
    {
        var (_, mapViewState, mapWindow, _, _) = BuildMapWindowWithPlayerAndActions(300, 300, 1, new Vector3Int(100, 100, 0));

        mapWindow.HandleRightClickTap();
        mapWindow.HandleEscape();

        Assert.IsNull(mapViewState.ArmedActionId);
    }

    private static readonly Guid TestPotionId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TestNonConsumableItemId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestNonSelfPotionId = new("77777777-7777-7777-7777-777777777777");

    private static void RegisterTestPotion(ItemCatalog itemCatalog) =>
        itemCatalog.Register(new ItemDefinition(
            TestPotionId, "Test Potion", null, "p", Color.Green, Tags: [Tag.Self],
            Effects: [new ActionEffect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

    /// <summary>Same PotionActivator/Burst shape as RegisterTestPotion, but deliberately untagged Self -- covers the double-tap shortcut now being keyed off Tag.Self rather than any particular IActionActivator kind.</summary>
    private static void RegisterTestNonSelfPotion(ItemCatalog itemCatalog) =>
        itemCatalog.Register(new ItemDefinition(
            TestNonSelfPotionId, "Test Non-Self Potion", null, "p", Color.Green, Tags: [],
            Effects: [new ActionEffect([new DirectHeal(0.5f)])],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

    private static void RegisterTestNonConsumableItem(ItemCatalog itemCatalog) =>
        itemCatalog.Register(new ItemDefinition(TestNonConsumableItemId, "Test Hammer", null, "h", Color.Gray, Tags: [], Effects: []));

    [TestMethod]
    public void HandleHotkeys_PressingBoundItemSlot_ArmsIt()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.AreEqual(TestPotionId, mapViewState.ArmedItemDefinitionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
        Assert.IsNull(mapViewState.ArmedActionId);
    }

    /// <summary>Same re-press-confirms rhythm as HandleHotkeys_PressingArmedSlotAgainAfterDoubleTapWindowElapses_ConfirmsAgainstHoveredTile, for an item slot.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingArmedItemSlotAgainAfterDoubleTapWindowElapses_ConfirmsAgainstHoveredTile()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());
        Assert.IsNotNull(mapViewState.ArmedItemDefinitionId);

        for (var i = 0; i < 20; i++)
        {
            mapWindow.Update(new GameTime());
        }

        // The caster's own tile -- within the Potion's Burst/3/1 footprint.
        mapWindow.UpdateHoveredTile(ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(100, 100, 0)));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        var pendingActivations = componentManager.GetPackedPool<PendingConsumableActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        Assert.AreEqual(TestPotionId, pendingActivations.GetReadonly(PlayerEntityId).ItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    /// <summary>Item counterpart to HandleHotkeys_PressingArmedSlotAgainWithNoHoveredTile_DoesNothingAndStaysArmed.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingArmedItemSlotAgainWithNoHoveredTile_DoesNothingAndStaysArmed()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());
        Assert.IsNotNull(mapViewState.ArmedItemDefinitionId);

        for (var i = 0; i < 20; i++)
        {
            mapWindow.Update(new GameTime());
        }

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(PlayerEntityId));
        Assert.IsNotNull(mapViewState.ArmedItemDefinitionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void HandleHotkeys_PressingBoundNonConsumableItemSlot_DoesNothing()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestNonConsumableItem(itemCatalog);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestNonConsumableItemId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    /// <summary>A user-friendly parallel to the inventory grid deleting the item once its last use is consumed -- a bound-but-out-of-stock slot must not arm, the same inert no-op a non-consumable (Hammer) slot already is.</summary>
    [TestMethod]
    public void HandleHotkeys_PressingBoundPotionSlotWithNoRemainingStock_DoesNothing()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        // Deliberately no InventoryActions.AddItem -- the entity has never had (or has fully
        // consumed) this item.
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    /// <summary>Two presses with no Update call in between read as a double-tap -- for a Potion, this always activates against the caster's own tile, never the arm/target flow.</summary>
    [TestMethod]
    public void HandleHotkeys_DoubleTapPotionSlot_QueuesSelfActivation_AndClearsAnyArming()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        var pendingActivations = componentManager.GetPackedPool<PendingConsumableActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(TestPotionId, pending.ItemDefinitionId);
        Assert.HasCount(1, pending.TargetTiles);
        Assert.AreEqual(new Vector3Int(100, 100, 0), pending.TargetTiles[0]);

        Assert.IsNull(mapViewState.ArmedItemDefinitionId, "The first press of the pair armed this slot -- once the double-tap fires, it shouldn't be left stale-armed.");
    }

    /// <summary>The double-tap self-cast shortcut is keyed off Tag.Self, not any particular IActionActivator kind -- a Potion item that isn't tagged Self (e.g. today's Health/Mana Potion before this fix) no longer gets it, and instead just arms normally like any other slot.</summary>
    [TestMethod]
    public void HandleHotkeys_DoubleTapNonSelfPotionSlot_ArmsInsteadOfSelfActivating()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestNonSelfPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestNonSelfPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestNonSelfPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsFalse(componentManager.GetPackedPool<PendingConsumableActivationComponent>().Has(PlayerEntityId), "No self-cast shortcut without Tag.Self -- a second rapid press just confirms against the cursor, which has no hovered tile here, so nothing should queue.");
        Assert.AreEqual(TestNonSelfPotionId, mapViewState.ArmedItemDefinitionId, "Still armed -- the second press had nothing to confirm against.");
    }

    [TestMethod]
    public void HandleClick_ArmedItem_ClickWithinFootprint_QueuesConsumableActivationAndDisarms()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        // Within the Potion's Range 3, so within TargetableTiles.
        var clickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(102, 100, 0));
        mapWindow.HandleClick(clickPosition);

        var pendingActivations = componentManager.GetPackedPool<PendingConsumableActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        Assert.AreEqual(TestPotionId, pendingActivations.GetReadonly(PlayerEntityId).ItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedItemDefinitionId, "Confirming a target must disarm.");
        Assert.IsNull(mapViewState.TargetableTiles);
    }

    /// <summary>
    /// Clicking your own tile with a Potion armed must produce the exact same single-tile,
    /// no-splash result double-tap already does (see HandleHotkeys_DoubleTapPotionSlot_QueuesSelfActivation_AndClearsAnyArming)
    /// -- not the real Burst/AreaSize splash the shape would otherwise resolve to when centered
    /// on yourself, which is what made "drink by double-tap" and "drink by clicking your own
    /// tile" behave differently before this fix.
    /// </summary>
    [TestMethod]
    public void HandleClick_ArmedItem_ClickOwnTile_QueuesSelfOnlyActivation_NotTheRealSplashShape()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        var clickPosition = ComputeScreenPositionForMapPosition(mapWindow, mapViewState, new Vector3Int(100, 100, 0));
        mapWindow.HandleClick(clickPosition);

        var pendingActivations = componentManager.GetPackedPool<PendingConsumableActivationComponent>();
        Assert.IsTrue(pendingActivations.Has(PlayerEntityId));
        var pending = pendingActivations.GetReadonly(PlayerEntityId);
        Assert.AreEqual(TestPotionId, pending.ItemDefinitionId);
        Assert.HasCount(1, pending.TargetTiles, "A Potion's real Burst/AreaSize:1 shape centered on the caster would resolve to 5 tiles -- clicking your own tile must bypass that and self-target only.");
        Assert.AreEqual(new Vector3Int(100, 100, 0), pending.TargetTiles[0]);
        Assert.IsNull(mapViewState.ArmedItemDefinitionId, "Confirming a target must disarm.");
    }

    [TestMethod]
    public void HandleRightClickTap_ArmedItem_CancelsIt()
    {
        var (_, mapViewState, mapWindow, componentManager, itemCatalog) = BuildMapWindowWithPlayerAndItems(300, 300, 1, new Vector3Int(100, 100, 0));
        RegisterTestPotion(itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        mapWindow.HandleRightClickTap();

        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
        Assert.IsNull(mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void HandleHotkeys_ArmingItemSlot_WhileActionArmed_DisarmsTheActionFirst()
    {
        var actionCatalog = new ActionCatalog();
        RegisterTestAdjacentAction(actionCatalog);
        var itemCatalog = new ItemCatalog();
        RegisterTestPotion(itemCatalog);
        var (_, mapViewState, mapWindow, componentManager) = BuildMapWindowCore(300, 300, 1, new Vector3Int(100, 100, 0), actionCatalog, itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());

        Assert.IsNull(mapViewState.ArmedActionId);
        Assert.AreEqual(TestPotionId, mapViewState.ArmedItemDefinitionId);
        Assert.AreEqual(HotkeySlot.Slot1, mapViewState.ArmedSlot);
    }

    [TestMethod]
    public void HandleHotkeys_ArmingActionSlot_WhileItemArmed_DisarmsTheItemFirst()
    {
        var actionCatalog = new ActionCatalog();
        RegisterTestAdjacentAction(actionCatalog);
        var itemCatalog = new ItemCatalog();
        RegisterTestPotion(itemCatalog);
        var (_, mapViewState, mapWindow, componentManager) = BuildMapWindowCore(300, 300, 1, new Vector3Int(100, 100, 0), actionCatalog, itemCatalog);
        InventoryActions.AddItem(componentManager, PlayerEntityId, TestPotionId, quantity: 1);
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(TestActionId, damageAmount: 10, cooldownFramesRemaining: 0));
        componentManager.Merge(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot4, TestActionId));
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot1, TestPotionId));

        mapWindow.HandleHotkeys(new KeyboardState(Keys.D1), new KeyboardState());
        mapWindow.HandleHotkeys(new KeyboardState(Keys.D4), new KeyboardState());

        Assert.IsNull(mapViewState.ArmedItemDefinitionId);
        Assert.AreEqual(TestActionId, mapViewState.ArmedActionId);
        Assert.AreEqual(HotkeySlot.Slot4, mapViewState.ArmedSlot);
    }
}