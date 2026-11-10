using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Containers;
using Game.Modules.Core.Components;
using Presentation.Input;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.MapRendering;
using Presentation.UI.FloatingText;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.UI;

/// <summary>Displays a scrollable/zoomable viewport onto a single MapLayer of the game map at a time.</summary>
/// <remarks>
/// Hosts the map: MapRenderer draws the world, this window draws the targeting and selection
/// highlights and floating text over it, and routes its own input hooks (hotkeys, clicks, right-drag)
/// to whichever collaborator actually owns that concern -- MapCamera (pan/zoom), ActionTargetingController
/// (arm/target/confirm), PlayerMovementController (WASD movement). MapWindow itself should stay thin glue
/// over those, not accumulate gameplay logic of its own.
///
/// Reads the world only through IMapViewQuery (PLAN-presentation-data-layer.md, Stage 2) -- never a
/// component pool. MapWindowArchitectureTests enforces that.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MapWindow : Window, IContentDropTarget
{
    private readonly IMapViewQuery _mapView;
    private readonly PlayerActionGate _playerActionGate;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly ActionTargetingController _actionTargeting;
    private readonly PlayerMovementController _playerMovement;
    private readonly ContextMenuController _contextMenuController;
    private readonly AdminContextMenuOptions _adminContextMenuOptions;
    private readonly PointerState _pointerState;
    private readonly FloatingTextController _floatingTextController;
    private readonly FloatingTextRenderer _floatingTextRenderer;
    private readonly MapRenderer _renderer;
    private readonly MapGameOverlay _overlay;

    /// <summary>This frame's "an earlier hotkey handler already used this key" set -- cleared and repopulated every OnHotkeysAction call, before PlayerMovementController.HandleInput reads it. See that class's own doc comment for why this exists (today: Dodge's directional confirm claiming WASD ahead of plain movement).</summary>
    private readonly HashSet<Keys> _claimedKeysThisFrame = [];

    /// <summary>The entities whose drawn sprites overhangs fade over this frame (OverhangFade). A field so it isn't reallocated per frame.</summary>
    private readonly List<int> _watchedEntityIds = [];
    private readonly List<MapGroundMark> _groundMarks = [];

    private readonly int _tileDepth;

    /// <summary>Whether the simulation is currently paused.</summary>
    /// <remarks>Toggled by Space while this window holds focus (see OnHotkeysAction). GameLoop.Update gates EcsContext.Update on this flag -- see the "Pause modality" TODO item for why this is one of several independent, not-yet-generalized pause sources GameLoop currently OR's together.</remarks>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Lets Space's pause toggle (see OnHotkeysAction) check whether a TextBox is currently
    /// focused elsewhere and skip if so -- e.g. typing a space into a search box or the Quest
    /// Composer must never also pause the game. Settable rather than a constructor dependency
    /// since UiInputController (the actual source of truth for "what's focused") is built after
    /// MapWindow -- see ShellBootstrapper.Build's own ordering notes. Null (before that
    /// wiring runs, and in tests that construct a MapWindow directly) means "assume nothing else
    /// is focused," matching today's unconditional behavior.
    /// </summary>
    public Func<bool>? IsTextInputFocused { get; set; }

    /// <summary>Opens an entity's items in the window they open in, once TryOpenContainer has allowed it -- ItemWindowCoordinator.OpenContainer.</summary>
    /// <remarks>Settable because the coordinator is built after MapWindow. Null (in tests that build a MapWindow alone) offers no "Loot" or "Shop" and opens nothing.</remarks>
    public Action<int, ContainerWindowKind>? OnOpenContainerRequested { get; set; }

    /// <summary>Moves everything a chest or pile holds to the player once TryTakeAll has allowed it -- ItemWindowCoordinator's, over LootCommands.TakeAll. Null offers no "Take All".</summary>
    public Action<int>? OnTakeAllRequested { get; set; }

    /// <summary>
    /// Invoked whenever a map-tile click sets Basic inspection (see SelectMapNodes) or the
    /// right-click "Inspect" option sets Detail inspection (see TryOpenEntityContextMenuAt) --
    /// lets ShellBootstrapper un-minimize InspectionWindow without MapWindow needing a direct
    /// reference to it, the same settable-delegate shape OnOpenContainerRequested above already uses.
    /// </summary>
    public Action? OnInspectionOpened { get; set; }

    /// <summary>Internal, not private, so tests can inspect an opened corpse context menu (its option list, an option's Enabled state) without needing to also thread one through every existing MapWindow test helper's return tuple.</summary>
    internal ContextMenuController ContextMenuController => _contextMenuController;

    /// <summary>Constructs the map viewport, wired to the world/camera/targeting/movement collaborators it renders and delegates input to.</summary>
    public MapWindow(
        ElementServices services,
        IMapViewQuery mapView,
        PlayerActionGate playerActionGate,
        MapViewState mapViewState,
        EventBus eventBus,
        MapCamera camera,
        MapRenderer renderer,
        MapGameOverlay overlay,
        ActionTargetingController actionTargeting,
        PlayerMovementController playerMovement,
        ContextMenuController contextMenuController,
        FloatingTextController floatingTextController,
        FloatingTextRenderer floatingTextRenderer,
        AdminContextMenuOptions adminContextMenuOptions,
        PointerState pointerState) : base(services)
    {
        _mapView = mapView;
        _playerActionGate = playerActionGate;
        _mapViewState = mapViewState;

        _camera = camera;
        _renderer = renderer;
        _overlay = overlay;
        _actionTargeting = actionTargeting;
        _playerMovement = playerMovement;
        _contextMenuController = contextMenuController;
        _adminContextMenuOptions = adminContextMenuOptions;
        _pointerState = pointerState;
        _floatingTextController = floatingTextController;
        _floatingTextRenderer = floatingTextRenderer;

        // The renderer's cached images are told when the terrain under them changes -- World.SetTerrain
        // and World.SetStructure publish these for every runtime change, streaming per row.
        eventBus.Subscribe<TerrainChangedEvent>(_ => _renderer.InvalidateTerrain());
        eventBus.Subscribe<StructureChangedEvent>(_ => _renderer.InvalidateTerrain());
        eventBus.Subscribe<TerrainLoadedEvent>(loaded => _renderer.InvalidateTerrainIn(loaded.Area));
        eventBus.Subscribe<TerrainUnloadingEvent>(unloading => _renderer.InvalidateTerrainIn(unloading.Area));

        _tileDepth = mapView.Bounds.Depth;
    }

    /// <summary>One-time setup once this window's own content size is known -- camera sizing and the initial camera position.</summary>
    /// <remarks>Snaps the camera to the player's spawn position if it already exists at this point. In real gameplay the player already exists by the time this ever runs -- WorldSessionBootstrapper.Build spawns it before ShellBootstrapper.Build ever constructs a MapWindow -- but this still has to tolerate the player not existing, for a MapWindow built directly (e.g. tests) without going through that same sequence.</remarks>
    public override void Initialize()
    {
        base.Initialize();

        _camera.Initialize(ContentSize);

        SetCurrentMapLayer(_mapViewState.CurrentMapLayer);

        if (TryGetPlayerPosition(out var playerPosition))
        {
            SnapCameraToPlayer(playerPosition);
            _lastKnownPlayerPosition = playerPosition;
        }
    }

    /// <summary>Whether the camera re-centres on the player when they move: a right-drag that moves the map stops it, Home resumes it.</summary>
    private bool _cameraFollowsPlayer = true;

    /// <summary>The player's position as last seen by Update, which re-centres only when it changes.</summary>
    private Vector3Int _lastKnownPlayerPosition;

    /// <summary>Per-frame camera-follow and hover-tracking.</summary>
    /// <remarks>
    /// Re-centers the camera only when the player's own position actually changed since last
    /// frame (not unconditionally every frame) and only while the camera follows the player --
    /// a right-mouse drag decouples it until Home recouples it.
    /// </remarks>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _actionTargeting.Tick();
        _floatingTextController.Update();

        if (_camera.RefreshScrollLimitsIfBoundsChanged())
        {
            _renderer.InvalidateTerrain();
        }

        if (TryGetPlayerPosition(out var playerPosition) && playerPosition != _lastKnownPlayerPosition)
        {
            _lastKnownPlayerPosition = playerPosition;

            if (_cameraFollowsPlayer)
            {
                _camera.CenterCameraOn(playerPosition);
            }
        }

        // Over another window (the hotbar, a menu), the tile beneath it isn't hovered: the cursor isn't on the map. Nor is
        // one while something is dragged: a drop never confirms an armed action, so no preview invites one -- unless the
        // drag armed a drop (DragDropArming), whose preview shows where the release will put it.
        _actionTargeting.UpdateHoveredTile(IsPointerOver && (!_pointerState.ContentDrag.Visible || _mapViewState.ArmedDrop is not null) ? _pointerState.CursorPosition : null, ContentAbsolutePosition);

        // Last, after the camera-follow above, so a frame that re-centres rebuilds the renderer's
        // images against the new scroll position.
        _renderer.Update(_mapViewState.CurrentMapLayer);
    }

    /// <summary>
    /// Delegates to ActionTargetingController.UpdateHoveredTile -- kept as a method on MapWindow
    /// (internal, not private) since MapWindowTests exercises it directly the same way it does
    /// Window.HandleHotkeys, simulating a mouse position without a real OS cursor.
    /// </summary>
    internal void UpdateHoveredTile(Point? mousePosition) => _actionTargeting.UpdateHoveredTile(mousePosition, ContentAbsolutePosition);

    /// <summary>Read-only view of the armed ability's current hit-footprint -- see ActionTargetingController.HoveredFootprint.</summary>
    internal TileSet HoveredFootprint => _actionTargeting.HoveredFootprint;

    /// <inheritdoc cref="ActionTargetingController.IsArmedConfirmRefused"/>
    internal bool IsArmedConfirmRefused => _actionTargeting.IsArmedConfirmRefused;

    private bool TryGetPlayerPosition(out Vector3Int position)
    {
        var found = _mapView.TryGetOccupant(_mapView.PlayerEntityId, out var player);
        position = player.Position;
        return found;
    }

    private void SnapCameraToPlayer(Vector3Int position)
    {
        SetCurrentMapLayer(position.Z);
        _camera.CenterCameraOn(position);
    }

    /// <summary>The single place [0, _tileDepth - 1] clamping happens for MapViewState.CurrentMapLayer -- shared by ChangeLayer, SnapToPlayer, and Initialize's own re-clamp against whatever depth this particular Map turns out to have.</summary>
    private void SetCurrentMapLayer(int layer)
    {
        _mapViewState.CurrentMapLayer = System.Math.Clamp(layer, 0, _tileDepth - 1);
    }

    /// <summary>Draws one frame of the map viewport: the world (MapRenderer) with the selection's ring on its ground, then targeting highlights, then floating text.</summary>
    /// <remarks>The selection ring lies on the ground, so the renderer draws it under what stands there; targeting highlights come after the world so they land on whatever is on the tile -- terrain, an occupant's sprite or glyph, or glow -- with no depth buffer to sort them.</remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        OverhangFade.CollectWatchedEntities(_watchedEntityIds, _mapView.PlayerEntityId, _mapViewState.InspectionMode, _mapViewState.InspectedEntityId);
        _overlay.CollectGroundMarks(_groundMarks);
        _renderer.Draw(_mapViewState.CurrentMapLayer, _watchedEntityIds, _groundMarks);
        _overlay.Draw(spriteBatch, unitRectangle);
        _floatingTextRenderer.Draw(spriteBatch, _mapViewState.CurrentMapLayer);
    }

    /// <summary>Switches the single MapLayer this window renders, by delta layers.</summary>
    /// <remarks>
    /// Stored on MapViewState.CurrentMapLayer, not locally, so SelectionWindowContent can scope
    /// the inspector to the same layer this window is actually showing. Called from OnHotkeysAction (Page Up/Down).
    /// </remarks>
    /// <param name="delta">Layers to move by -- positive moves up, negative moves down (clamped to the map's own depth by SetCurrentMapLayer).</param>
    public void ChangeLayer(int delta)
    {
        SetCurrentMapLayer(_mapViewState.CurrentMapLayer + delta);
    }

    /// <summary>Sets the camera to a specific zoom level directly, as opposed to CycleZoom's relative +/-1 step.</summary>
    /// <remarks>No production caller yet (only MapWindowTests exercises this today) -- a candidate hook for a future zoom UI control (see the Minimap TODO item).</remarks>
    /// <param name="newZoomLevel">The zoom level to switch to.</param>
    public void UpdateZoomLevel(ZoomLevel newZoomLevel)
    {
        _camera.UpdateZoomLevel(newZoomLevel, ContentSize);
    }

    private void CycleZoom(int direction)
    {
        _camera.CycleZoom(direction, ContentSize);
    }

    /// <summary>Scrolls the camera by scrollChange tiles, clamped against the map edge.</summary>
    /// <param name="scrollChange">The requested scroll delta, in tiles.</param>
    public void UpdateScrollPosition(Point scrollChange)
    {
        _camera.UpdateScrollPosition(scrollChange);
    }

    /// <summary>Sets MapViewState.SelectedMapNodePosition to whatever map tile mousePosition resolves to, if any.</summary>
    /// <remarks>A miss (cursor off the map) is a no-op -- the previous selection, if any, stays selected rather than being cleared by clicking empty space. This is the ordinary inspector click-select path; see OnContentClickAction for why an armed ability/item's click-to-confirm takes over first when something is armed.</remarks>
    /// <param name="mousePosition">The raw mouse position (e.g. from Mouse.GetState()), not pre-translated to this window's content area -- resolved against ContentAbsolutePosition internally, the same as UpdateHoveredTile/TryConfirmActivation.</param>
    public void SelectMapNodes(Point mousePosition)
    {
        if (TryGetPointerHit(mousePosition, out var hit))
        {
            _mapViewState.SelectedMapNodePosition = new Point(hit.Tile.X, hit.Tile.Y);
            _mapViewState.InspectionMode = InspectionMode.Basic;
            _mapViewState.InspectedEntityId = -1;
            OnInspectionOpened?.Invoke();
        }
    }

    /// <summary>
    /// A left-click confirms the armed ability/item's activation if either is armed, falling
    /// back to the ordinary inspector click-select otherwise -- an armed ability or item's target
    /// selection takes over the click entirely while it's active, matching how the outline
    /// describes left-click as the universal "activate" gesture once something is armed.
    /// </summary>
    protected override bool OnContentClickAction(Point mousePosition)
    {
        _lastSelectedTile = null;
        if (_mapViewState.IsAnythingArmed)
        {
            _actionTargeting.TryConfirmActivation(mousePosition, ContentAbsolutePosition);
            return true;
        }

        SelectMapNodes(mousePosition);
        if (TryGetPointerHit(mousePosition, out var hit))
        {
            _lastSelectedTile = hit.Tile;
        }

        return true;
    }

    /// <summary>The tile the last click selected, when it was a plain select -- a double-click opens what is there only after one.</summary>
    private Vector3Int? _lastSelectedTile;

    /// <summary>A double-click on the tile the first click selected opens the topmost thing there whose items open in a window (TryOpenContainer); anything else is a second click.</summary>
    /// <remarks>Never after a click that confirmed an armed activation, so aiming twice in quick succession never also opens a container.</remarks>
    protected override bool OnContentDoubleClickAction(Point mousePosition)
    {
        if (_lastSelectedTile is not { } selectedTile || !TryGetPointerHit(mousePosition, out var hit) || hit.Tile != selectedTile)
        {
            return false;
        }

        _lastSelectedTile = null;
        foreach (var entityId in hit.OccupantEntityIds)
        {
            if (_mapView.GetInteraction(entityId).WindowKind != ContainerWindowKind.None)
            {
                return TryOpenContainer(entityId);
            }
        }

        return false;
    }

    /// <summary>Opens entityId's items in their window, if it has one the player can reach now (ContainerAccess, AllowsAccess) -- the one path the "Loot"/"Shop" option and a double-click both take.</summary>
    internal bool TryOpenContainer(int entityId)
    {
        var interaction = _mapView.GetInteraction(entityId);
        if (OnOpenContainerRequested is not { } openContainer || interaction.WindowKind == ContainerWindowKind.None || !AllowsAccess(interaction.AccessRefusal))
        {
            return false;
        }

        openContainer(entityId, interaction.WindowKind);
        return true;
    }

    /// <summary>A chest or an item pile -- a container whose items open in a loot window, not a corpse -- offers Take All on the map, as its loot window does.</summary>
    private static bool OffersTakeAll(in EntityInteractionView interaction) =>
        interaction.WindowKind == ContainerWindowKind.Loot && interaction.IsContainer;

    /// <summary>Whether Take All would take something now: it offers it, the player can reach it, and it holds anything -- the option's Enabled and TryTakeAll's check.</summary>
    private static bool CanTakeAll(in EntityInteractionView interaction) =>
        OffersTakeAll(in interaction) && AllowsAccess(interaction.AccessRefusal) && interaction.HoldsAnything;

    /// <summary>Takes everything entityId holds when it can (CanTakeAll) -- the map's "Take All", the same command as the loot window's (LootCommands.TakeAll).</summary>
    internal bool TryTakeAll(int entityId)
    {
        var interaction = _mapView.GetInteraction(entityId);
        if (OnTakeAllRequested is not { } takeAll || !CanTakeAll(in interaction))
        {
            return false;
        }

        takeAll(entityId);
        return true;
    }

    /// <summary>Whether the player may reach a container's items despite refusal: only when there is none, or Admin Mode overrides a loot reservation.</summary>
    internal static bool AllowsAccess(ContainerAccessRefusal refusal) =>
        refusal == ContainerAccessRefusal.None || (refusal == ContainerAccessRefusal.LootReserved && AdminMode.IsOn);

    /// <summary>The tile under screenPosition on the layer shown, and its occupants topmost first -- false off the map.</summary>
    /// <remarks>Asked every frame a drag is held over the map, so an unchanged tile with unchanged occupants hands back the hit it made last time.</remarks>
    internal bool TryGetPointerHit(Point screenPosition, out MapPointerHit hit)
    {
        if (!_camera.TryGetHoveredMapPosition(screenPosition, ContentAbsolutePosition, out var mapPosition))
        {
            hit = null!;
            return false;
        }

        var tile = new Vector3Int(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer);
        var occupants = _mapView.GetOccupants(tile);
        if (_lastPointerHit is { } lastHit && lastHit.Tile == tile && occupants.SequenceEqual(_lastPointerHitOccupants))
        {
            hit = lastHit;
            return true;
        }

        _lastPointerHitOccupants = occupants.ToArray();
        hit = new MapPointerHit(tile, OrderTopmostFirst(tile, occupants));
        _lastPointerHit = hit;
        return true;
    }

    private MapPointerHit? _lastPointerHit;

    /// <summary>The occupants _lastPointerHit was made from, in the map's own order, to tell when they changed.</summary>
    private int[] _lastPointerHitOccupants = [];

    private int[] OrderTopmostFirst(Vector3Int tile, ReadOnlySpan<int> occupants)
    {
        var ordered = occupants.ToArray();
        var ranks = new int[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            ranks[index] = DrawnRankFromTop(tile, ordered[index]) * ordered.Length + index;
        }

        Array.Sort(ranks, ordered);
        return ordered;
    }

    /// <summary>0 for what the map draws on top of a tile -- a Phasing occupant, then the Blocking one, then Tiny ones, then corpses -- and 4 for anything it doesn't draw.</summary>
    private int DrawnRankFromTop(Vector3Int tile, int entityId)
    {
        if (!_mapView.TryGetOccupant(entityId, out var occupant))
        {
            return 4;
        }

        if (occupant.IsDead)
        {
            return 3;
        }

        if ((occupant.Kind & NonBlockingKind.Phasing) != 0)
        {
            return 0;
        }

        if (_mapView.GetBlockingEntityId(tile) == entityId)
        {
            return 1;
        }

        return (occupant.Kind & NonBlockingKind.Tiny) != 0 ? 2 : 4;
    }

    /// <summary>A drop on the map lands on the tile under it and what stands there (MapPointerHit); off the map, nowhere.</summary>
    public object? DropTargetFor(object payload, Point position) => TryGetPointerHit(position, out var hit) ? hit : null;

    /// <summary>
    /// A right-click-tap first tries to cancel an armed/pending action (see
    /// CancelArmedOrPendingAction's own doc comment); only when that was a no-op -- nothing to
    /// cancel -- does it fall through to opening a corpse's context menu, the same "cancel wins
    /// if there's anything to cancel" precedence a left-click's own OnContentClickAction already
    /// gives TryConfirmActivation over SelectMapNodes.
    /// </summary>
    protected override bool OnRightClickTapAction(Point mousePosition)
    {
        if (!_actionTargeting.CancelArmedOrPendingAction())
        {
            TryOpenEntityContextMenuAt(mousePosition);
        }

        return true;
    }

    /// <summary>
    /// Opens a stacked context menu for everything on the tile under mousePosition -- every
    /// occupant (Blocking or not), each its own group, with the structure's then the terrain's name
    /// as last headers when there is at least one occupant group: a read-only name header (see DropdownOption.Header -- also the visual separator
    /// from the next group, no blank divider needed), "Loot" or "Shop" first if that entity's items
    /// open in a window (TryOpenContainer, which a double-click takes too), then always
    /// "Inspect" (Details/Admin inspection -- see MapViewState.InspectionMode's own doc comment
    /// on why Admin isn't a separate option yet). Internal, not private, so tests can simulate a
    /// right-click-tap at a specific screen position directly. A miss (off-map, nothing on the
    /// tile at all) simply opens nothing -- OnRightClickTapAction's only other job, cancelling an
    /// armed/pending action, already ran and found nothing to cancel either, so a right-click
    /// over empty space is correctly a total no-op, matching this codebase's "remove unexpected
    /// actions" principle. Each option is disabled rather than omitted when it can't currently be
    /// taken ("Loot" when the player can't reach it, "Inspect" while the global cooldown is
    /// active) so the player sees why, rather than the menu silently missing an option.
    /// </summary>
    internal void TryOpenEntityContextMenuAt(Point mousePosition)
    {
        if (!TryGetPointerHit(mousePosition, out var hit))
        {
            return;
        }

        var tilePosition = hit.Tile;
        var mapPosition = new Point(tilePosition.X, tilePosition.Y);

        List<DropdownOption> options = [];

        foreach (var entityId in hit.OccupantEntityIds)
        {
            AddEntityGroup(options, entityId);
        }

        // Structures and terrain are cells, not entities, so they offer nothing to loot, trade with or
        // inspect. They still get named headers beneath the occupants' groups so the menu says what
        // they stand among and on -- but never a menu of their own, which would be a menu with
        // nothing in it to choose.
        if (options.Count > 0 && _mapView.TryGetStructure(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer, out var structure))
        {
            options.Add(DropdownOption.Header(structure.Name));
        }

        if (options.Count > 0 && _mapView.TryGetTerrain(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer, out var terrain))
        {
            options.Add(DropdownOption.Header(terrain.Name));
        }

        if (AdminMode.IsOn)
        {
            _adminContextMenuOptions.AddTileOptions(options, tilePosition);
        }

        if (options.Count > 0)
        {
            _contextMenuController.Open(new Vector2(mousePosition.X, mousePosition.Y), options);
        }
    }

    /// <summary>Appends one occupant's own group to the tile's stacked menu -- a read-only name header, then whatever options it offers.</summary>
    private void AddEntityGroup(List<DropdownOption> options, int entityId)
    {
        var interaction = _mapView.GetInteraction(entityId);
        options.Add(DropdownOption.Header(interaction.Name));

        if (interaction.WindowKind != ContainerWindowKind.None && OnOpenContainerRequested is not null)
        {
            options.Add(new DropdownOption(OpenOptionLabel(interaction), null, AllowsAccess(interaction.AccessRefusal), () => TryOpenContainer(entityId)));
        }

        if (OffersTakeAll(in interaction) && OnTakeAllRequested is not null)
        {
            options.Add(new DropdownOption("Take All", null, CanTakeAll(in interaction), () => TryTakeAll(entityId)));
        }

        options.Add(new DropdownOption("Inspect", null, !_playerActionGate.IsLocked, () => InspectEntity(entityId)));

        if (AdminMode.IsOn)
        {
            _adminContextMenuOptions.AddEntityOptions(options, entityId);
        }
    }

    /// <summary>"Shop" for a standing shop, otherwise "Loot" -- "Loot (reserved)" while its loot is someone else's and Admin Mode doesn't override it.</summary>
    private static string OpenOptionLabel(in EntityInteractionView interaction)
    {
        if (interaction.WindowKind == ContainerWindowKind.Shop)
        {
            return "Shop";
        }

        return interaction.AccessRefusal == ContainerAccessRefusal.LootReserved && !AdminMode.IsOn ? "Loot (reserved)" : "Loot";
    }

    /// <summary>Details/Admin inspection's actual activation -- sets Detail or Admin mode (AdminMode.IsOn) on the shared entityId (see MapViewState.InspectedEntityId), starts the global cooldown (the same shared ActionLockComponent lock movement/melee/consumables already use), and un-minimizes InspectionWindow. Only ever reached via the "Inspect" DropdownOption above, which already gates on the cooldown being clear -- no redundant re-check here, matching how "Loot" above trusts its own Enabled gate instead of re-checking adjacency.</summary>
    private void InspectEntity(int entityId)
    {
        _mapViewState.InspectionMode = AdminMode.IsOn ? InspectionMode.Admin : InspectionMode.Detail;
        _mapViewState.InspectedEntityId = entityId;
        _playerActionGate.Lock();
        OnInspectionOpened?.Invoke();
    }

    protected override void OnEscapeAction() => _actionTargeting.CancelArmedOrPendingAction();

    /// <summary>The map's own hotkeys -- only invoked while this window holds focus (see UiInputController.RouteHotkeysToFocusedWindow).</summary>
    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Space) && !(IsTextInputFocused?.Invoke() ?? false))
        {
            IsPaused = !IsPaused;
        }

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Home))
        {
            _cameraFollowsPlayer = true;
            if (TryGetPlayerPosition(out var playerPosition))
            {
                SnapCameraToPlayer(playerPosition);
            }
        }

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.OemPlus) || WasKeyPressed(keyboardState, previousKeyboardState, Keys.Add))
        {
            CycleZoom(-1);
        }
        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.OemMinus) || WasKeyPressed(keyboardState, previousKeyboardState, Keys.Subtract))
        {
            CycleZoom(1);
        }

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.PageUp))
        {
            ChangeLayer(1);
        }
        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.PageDown))
        {
            ChangeLayer(-1);
        }

        if (!IsPaused)
        {
            _claimedKeysThisFrame.Clear();
            _actionTargeting.TryClaimDodgeDirectionalKey(keyboardState, previousKeyboardState, _claimedKeysThisFrame);
            _playerMovement.HandleInput(keyboardState, previousKeyboardState, _claimedKeysThisFrame);
            _actionTargeting.HandleHotbarHotkeys(keyboardState, previousKeyboardState);
        }
    }

    /// <summary>Snapshots the scroll position the moment a right-mouse-drag starts, so OnRightDragAction always has a fixed anchor to measure the drag against.</summary>
    protected override void OnRightDragStartAction() => _camera.BeginDrag();

    protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart)
    {
        if (totalPixelDeltaSinceStart == Vector2.Zero)
        {
            return;
        }

        _cameraFollowsPlayer = false;
        var scrollChange = _camera.ApplyDrag(totalPixelDeltaSinceStart);
        if (scrollChange != Point.Zero)
        {
            UpdateScrollPosition(scrollChange);
        }
    }

    protected override void OnRightDragEndAction()
    {
        var snap = _camera.EndDrag();
        if (snap != Point.Zero)
        {
            UpdateScrollPosition(snap);
        }
    }
}
