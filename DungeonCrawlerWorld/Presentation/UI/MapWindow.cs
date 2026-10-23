using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Presentation.Input;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.MapRendering;
using Presentation.UI.FloatingText;
using UIEngine.Fonts;
using UIEngine.Rendering;
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
public sealed class MapWindow : Window
{
    private readonly IMapViewQuery _mapView;
    private readonly PlayerActionGate _playerActionGate;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly ActionTargetingController _actionTargeting;
    private readonly PlayerMovementController _playerMovement;
    private readonly ContextMenuController _contextMenuController;
    private readonly AdminContextMenuOptions _adminContextMenuOptions;
    private readonly FloatingTextController _floatingTextController;
    private readonly FloatingTextRenderer _floatingTextRenderer;
    private readonly MapRenderer _renderer;
    private readonly MapGameOverlay _overlay;

    /// <summary>This frame's "an earlier hotkey handler already used this key" set -- cleared and repopulated every OnHotkeysAction call, before PlayerMovementController.HandleInput reads it. See that class's own doc comment for why this exists (today: Dodge's directional confirm claiming WASD ahead of plain movement).</summary>
    private readonly HashSet<Keys> _claimedKeysThisFrame = [];

    /// <summary>The entities whose drawn sprites overhangs fade over this frame (OverhangFade). A field so it isn't reallocated per frame.</summary>
    private readonly List<int> _watchedEntityIds = [];

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

    /// <summary>
    /// Invoked with a corpse's entity id when the player selects "Loot" from its right-click
    /// context menu (see TryOpenEntityContextMenuAt). Settable rather than a constructor
    /// dependency for the same reason IsTextInputFocused above is: the real listener
    /// (ItemWindowCoordinator.OpenLoot) is built after MapWindow. Null (before that wiring runs,
    /// and in tests that construct a MapWindow directly) means right-clicking a corpse opens no
    /// context menu at all, rather than one with a "Loot" option that does nothing.
    /// </summary>
    public Action<int>? OnCorpseClicked { get; set; }

    /// <summary>Invoked with a shop's entity id when the player selects "Shop" from its right-click context menu (see AddEntityGroup) -- same settable-delegate shape as OnCorpseClicked, wired by ItemWindowCoordinator to its OpenShop.</summary>
    public Action<int>? OnShopClicked { get; set; }

    /// <summary>
    /// Invoked whenever a map-tile click sets Basic inspection (see SelectMapNodes) or the
    /// right-click "Inspect" option sets Detail inspection (see TryOpenEntityContextMenuAt) --
    /// lets ShellBootstrapper un-minimize InspectionWindow without MapWindow needing a direct
    /// reference to it, the same settable-delegate shape OnCorpseClicked above already uses.
    /// </summary>
    public Action? OnInspectionOpened { get; set; }

    /// <summary>Internal, not private, so tests can inspect an opened corpse context menu (its option list, an option's Enabled state) without needing to also thread one through every existing MapWindow test helper's return tuple.</summary>
    internal ContextMenuController ContextMenuController => _contextMenuController;

    /// <summary>Constructs the map viewport, wired to the world/camera/targeting/movement collaborators it renders and delegates input to.</summary>
    public MapWindow(
        FontService fontService,
        ElementPoolService elementPoolService,
        IMapViewQuery mapView,
        PlayerActionGate playerActionGate,
        MapViewState mapViewState,
        EventBus eventBus,
        UiRenderService renderService,
        MapCamera camera,
        MapRenderer renderer,
        MapGameOverlay overlay,
        ActionTargetingController actionTargeting,
        PlayerMovementController playerMovement,
        ContextMenuController contextMenuController,
        FloatingTextController floatingTextController,
        FloatingTextRenderer floatingTextRenderer,
        AdminContextMenuOptions adminContextMenuOptions) : base(fontService, elementPoolService, renderService)
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

        var mouseState = Mouse.GetState();
        UpdateHoveredTile(new Point(mouseState.X, mouseState.Y));

        // Last, after the camera-follow above, so a frame that re-centres rebuilds the renderer's
        // images against the new scroll position.
        _renderer.Update(_mapViewState.CurrentMapLayer);
    }

    /// <summary>
    /// Delegates to ActionTargetingController.UpdateHoveredTile -- kept as a method on MapWindow
    /// (internal, not private) since MapWindowTests exercises it directly the same way it does
    /// Window.HandleHotkeys, simulating a mouse position without a real OS cursor.
    /// </summary>
    internal void UpdateHoveredTile(Point mousePosition) => _actionTargeting.UpdateHoveredTile(mousePosition, ContentAbsolutePosition);

    /// <summary>Read-only view of the armed ability's current hit-footprint -- see ActionTargetingController.HoveredFootprint.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _actionTargeting.HoveredFootprint;

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

    /// <summary>Draws one frame of the map viewport: the world (MapRenderer), then targeting and selection highlights, then floating text.</summary>
    /// <remarks>Highlights come after the world so they land on whatever is on the tile -- terrain, an occupant's sprite or glyph, or glow -- with no depth buffer to sort them.</remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.SpriteBatch;
        var unitRectangle = Render.UnitRectangle;

        OverhangFade.CollectWatchedEntities(_watchedEntityIds, _mapView.PlayerEntityId, _mapViewState.InspectionMode, _mapViewState.InspectedEntityId);
        _renderer.Draw(_mapViewState.CurrentMapLayer, _watchedEntityIds);
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
        if (_camera.TryGetHoveredMapPosition(mousePosition, ContentAbsolutePosition, out var mapPosition))
        {
            _mapViewState.SelectedMapNodePosition = mapPosition;
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
    protected override void OnContentClickAction(Point mousePosition)
    {
        if (_mapViewState.ArmedActionId is not null || _mapViewState.ArmedItemStackInstanceId is not null)
        {
            _actionTargeting.TryConfirmActivation(mousePosition, ContentAbsolutePosition);
            return;
        }

        SelectMapNodes(mousePosition);
    }

    /// <summary>A player can only loot a corpse they're standing on or next to -- 8-directional (Chebyshev) distance of at most 1 from the corpse's own origin tile, the same adjacency shape TargetShape.Adjacent's ring uses elsewhere, just inclusive of the caster's own tile too (unlike melee's ring, which excludes it -- standing on a corpse to loot it is expected, unlike punching yourself).</summary>
    private bool IsAdjacentToPlayer(int entityId) =>
        _mapView.TryGetOccupant(entityId, out var target) &&
        TryGetPlayerPosition(out var playerPosition) &&
        GridDistance.ChebyshevDistance(target.Position, playerPosition) <= 1;

    /// <summary>
    /// A right-click-tap first tries to cancel an armed/pending action (see
    /// CancelArmedOrPendingAction's own doc comment); only when that was a no-op -- nothing to
    /// cancel -- does it fall through to opening a corpse's context menu, the same "cancel wins
    /// if there's anything to cancel" precedence a left-click's own OnContentClickAction already
    /// gives TryConfirmActivation over SelectMapNodes.
    /// </summary>
    protected override void OnRightClickTapAction(Point mousePosition)
    {
        if (_actionTargeting.CancelArmedOrPendingAction())
        {
            return;
        }

        TryOpenEntityContextMenuAt(mousePosition);
    }

    /// <summary>
    /// Opens a stacked context menu for everything on the tile under mousePosition -- every
    /// occupant (Blocking or not), each its own group, with the structure's then the terrain's name
    /// as last headers when there is at least one occupant group: a read-only name header (see DropdownOption.Header -- also the visual separator
    /// from the next group, no blank divider needed), "Loot" first if that entity is a corpse or
    /// a container (a container is lootable even while alive -- see ContainerComponent's own doc
    /// comment; replaces the old click-to-loot, see OnCorpseClicked's own doc comment), then always
    /// "Inspect" (Details/Admin inspection -- see MapViewState.InspectionMode's own doc comment
    /// on why Admin isn't a separate option yet). Internal, not private, so tests can simulate a
    /// right-click-tap at a specific screen position directly. A miss (off-map, nothing on the
    /// tile at all) simply opens nothing -- OnRightClickTapAction's only other job, cancelling an
    /// armed/pending action, already ran and found nothing to cancel either, so a right-click
    /// over empty space is correctly a total no-op, matching this codebase's "remove unexpected
    /// actions" principle. Each option is disabled rather than omitted when it can't currently be
    /// taken ("Loot" when the player isn't adjacent, "Inspect" while the global cooldown is
    /// active) so the player sees why, rather than the menu silently missing an option.
    /// </summary>
    internal void TryOpenEntityContextMenuAt(Point mousePosition)
    {
        if (!_camera.TryGetHoveredMapPosition(mousePosition, ContentAbsolutePosition, out var mapPosition))
        {
            return;
        }

        var tilePosition = new Vector3Int(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer);

        List<DropdownOption> options = [];

        foreach (var entityId in _mapView.GetOccupants(tilePosition))
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

        var isShop = interaction.IsShop;

        // A shop that's died goes through the same EntityDiedEvent -> DeadComponent pipeline as any
        // other SimpleHealthComponent entity (DeathSystem doesn't special-case containers/shops out
        // of it -- see its own doc comment), even though ContainerDestructionSystem's own handling
        // for the same event leaves its ShopComponent/ContainerComponent in place (renaming it
        // "Destroyed" and clearing its stock, not removing the components). So isShop alone can't
        // tell a live shop apart from a destroyed one -- isDestroyed does.
        var isDestroyed = interaction.IsDestroyed;

        // A shop is still a ContainerComponent (see Shop's own doc comment), but gets its own
        // "Shop" verb instead of the generic corpse/chest "Loot" one while it's alive -- excluded
        // here so a live shop never offers both. A destroyed shop is the opposite: "Shop" no longer
        // makes sense (nothing left to trade), so it falls through to the plain "Loot" a dead
        // creature or a destroyed chest already gets.
        if ((isDestroyed || (interaction.IsContainer && !isShop)) && OnCorpseClicked is { } onCorpseClicked)
        {
            var isLootReserved = interaction.IsLootReservedFromPlayer && !AdminMode.IsOn;
            options.Add(new DropdownOption(isLootReserved ? "Loot (reserved)" : "Loot", null, IsAdjacentToPlayer(entityId) && !isLootReserved, () => onCorpseClicked.Invoke(entityId)));
        }

        if (isShop && !isDestroyed && OnShopClicked is { } onShopClicked)
        {
            options.Add(new DropdownOption("Shop", null, IsAdjacentToPlayer(entityId), () => onShopClicked.Invoke(entityId)));
        }

        options.Add(new DropdownOption("Inspect", null, !_playerActionGate.IsLocked, () => InspectEntity(entityId)));

        if (AdminMode.IsOn)
        {
            _adminContextMenuOptions.AddEntityOptions(options, entityId);
        }
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
