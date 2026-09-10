using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Containers.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>Displays a scrollable/zoomable viewport onto a single MapLayer of the game map at a time.</summary>
/// <remarks>
/// The map-rendering composition root: owns the draw order (background, glyphs/sprites, glow,
/// targeting/selection highlights) and routes its own input hooks (hotkeys, clicks, right-drag)
/// to whichever collaborator actually owns that concern -- MapCamera (pan/zoom), MapBackgroundCache
/// (per-tile background color), MapTintGrid (aura glow), ActionTargetingController (arm/target/
/// confirm), PlayerMovementController (WASD movement). MapWindow itself should stay thin glue
/// over those, not accumulate gameplay logic of its own.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MapWindow : Window
{
    private const int MaxTinyEntitiesDrawn = 9;
    private const int TinyGridDimension = 3;
    private static readonly Color UpLayerBadgeColor = Color.Blue;
    private static readonly Color DownLayerBadgeColor = new(101, 67, 33);

    private const float HealthBarWidthFraction = 0.9f;
    private const int HealthBarHeightPixels = 4;

    /// <summary>Fraction of a single tile's own size (not the corpse's own possibly-multi-tile footprint) -- anchored to the tile's top-right corner the same way DrawLayerBadges' up/down arrows are.</summary>
    private const float LootBagBadgeSizeFraction = 0.4f;
    private const string LootBagSpriteName = "LootBag-Red";

    /// <summary>Color of a dodging entity's own inner-fade glow -- Combat Overhaul: Dodge.</summary>
    private static readonly Color DodgingGlowColor = Color.LightGreen;

    /// <summary>Boosts GlowRenderer's own 50%-at-the-edge default toward fully opaque -- see the call site's own doc comment for why the un-boosted default read as invisible.</summary>
    private const float DodgingGlowAlphaMultiplier = 2f;

    /// <summary>Fraction of a tile's own size the charging-action badge renders at, matching LootBagBadgeSizeFraction's own scale.</summary>
    private const float ChargingBadgeSizeFraction = 0.4f;

    /// <summary>Fraction of TargetSelectionMaskAlpha used for the always-visible full-tile backdrop drawn under the growing charge fill -- keeps a multi-tile Delayed action's whole target shape legible from the first frame of the windup, not just once each tile's own fill has grown enough to be seen.</summary>
    private const float ChargeBackdropAlphaFraction = 0.3f;

    private readonly World _world;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly ActionTargetingController _actionTargeting;
    private readonly PlayerMovementController _playerMovement;
    private readonly ContextMenuController _contextMenuController;
    private readonly MapBackgroundCache _backgroundCache;

    /// <summary>Null until the first Update that finds a GraphicsDevice on ElementPoolService, and permanently null for a headless MapWindow that never gets one -- DrawContent's own fallback paths cover both cases. See Update for why these can't be built in Initialize.</summary>
    private MapTileLayerCache? _terrainCache;

    /// <summary>The aura glow overlay's own cached rendering -- a separate texture from _terrainCache rather than the same one because it is blitted on the other side of the occupants (see DrawGlowOverlay).</summary>
    private MapTileLayerCache? _glowCache;

    /// <summary>The MapTintGrid.Version _glowCache was last rendered against -- the glow texture depends on the tint grid's contents as well as on the camera, so a source appearing, moving or expiring has to invalidate it even though nothing about the camera changed.</summary>
    private int _renderedGlowVersion = -1;

    private readonly MapTintGrid _tintGrid;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly DirectComponentPool<SpriteComponent> _spritePool;
    private readonly MultiComponentPool<NonBlockingComponent> _nonBlockingPool;
    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool;
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadPool;
    private readonly MultiComponentPool<InventoryItemStackComponent>? _inventoryStacks;
    private readonly PackedComponentPool<LootedComponent>? _lootedPool;
    private readonly PackedComponentPool<ContainerComponent>? _containerPool;
    private readonly PackedComponentPool<ShopComponent>? _shopPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLockPool;
    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly ActionCatalog _actionCatalog;

    /// <summary>This frame's "an earlier hotkey handler already used this key" set -- cleared and repopulated every OnHotkeysAction call, before PlayerMovementController.HandleInput reads it. See that class's own doc comment for why this exists (today: Dodge's directional confirm claiming WASD ahead of plain movement).</summary>
    private readonly HashSet<Keys> _claimedKeysThisFrame = [];

    /// <summary>Per-entity real-elapsed-frames-since-charge-started, backing TrackChargeElapsedFraction -- see that method's own doc comment for why this counts elapsed time rather than reading ActionLockComponent's own stepped countdown. Pruned every DrawTargetingHighlights call by PruneChargeFillSmoothingState.</summary>
    private readonly Dictionary<int, float> _chargeFillElapsedFrames = [];

    /// <summary>Reused by PruneChargeFillSmoothingState -- avoids a per-frame allocation to collect the entity ids to remove while iterating _chargeFillElapsedFrames' own keys (can't Remove mid-iteration).</summary>
    private readonly List<int> _staleChargeFillEntityIdsBuffer = [];

    /// <summary>Reused by PruneChargeFillSmoothingState -- the current frame's charging-entity ids, built once so membership checks are O(1) instead of a linear re-scan per stale-candidate.</summary>
    private readonly HashSet<int> _activeChargingEntityIdsBuffer = [];

    private readonly TileRenderer _tileRenderer;
    private readonly LabelRenderer _labelRenderer;
    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

    private const float TargetSelectionMaskAlpha = 0.5f;

    /// <summary>Halves MapTintGrid's own already-falloff-scaled Factor so a full-strength aura glow (Factor 1) still lets whatever's standing on that tile -- terrain, an occupant sprite/glyph -- read through, rather than washing it out at the source tile itself.</summary>
    private const float GlowOpacityMultiplier = 0.5f;

    private static readonly Color MapBackgroundColor = new(40, 40, 40);

    private SpriteFontBase _mediumFont = null!;
    private SpriteFontBase _largeFont = null!;
    private SpriteFontBase _hugeFont = null!;
    private SpriteFontBase _tinyFont = null!;
    private SpriteFontBase _badgeFont = null!;

    /// <summary>
    /// LootBag-Red resolved once, not per badge per frame -- SpriteManifest.TryGetFirst is a
    /// string-keyed dictionary lookup, and repeating it for every corpse badge of every frame is
    /// needless work on the draw path. TryGetFirst rather than TryGetRandom because a badge has to
    /// look the same on every corpse: rolling among LootBag-Red's candidate cells here would bake
    /// one arbitrary variant in per session, which is invisible only while that entry has exactly
    /// one cell.
    /// </summary>
    private SpriteComponent? _lootBagSprite;

    /// <summary>This tile's Phasing occupants, collected during DrawUnderlayOccupants' single walk and drawn by DrawPhasingOverlay once the Blocking occupant is down. A field rather than a local so it isn't reallocated for every visible tile of every frame; cleared at the start of each tile.</summary>
    private readonly List<int> _phasingOccupantsBuffer = [];

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
    /// (SecondaryInventoryWindowController) is built after MapWindow -- see ShellBootstrapper.
    /// Build's own ordering notes. Null (before that wiring runs, and in tests that construct a
    /// MapWindow directly) means right-clicking a corpse opens no context menu at all, rather
    /// than one with a "Loot" option that does nothing.
    /// </summary>
    public Action<int>? OnCorpseClicked { get; set; }

    /// <summary>Invoked with a shop's entity id when the player selects "Shop" from its right-click context menu (see AddEntityGroup) -- same settable-delegate shape as OnCorpseClicked, wired by ShellBootstrapper to ShopWindowController.OpenShop.</summary>
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
    /// <remarks>
    /// MapTintGrid and MapBackgroundCache are constructed here, not injected, unlike every other
    /// dependency -- both are MapWindow-private derived state (a per-cell glow index, a per-cell
    /// background-color cache) with no other consumer, so there's nothing to gain from resolving
    /// them through ShellBootstrapper the way the shared services above are.
    /// </remarks>
    public MapWindow(
        FontService fontService,
        ElementPoolService elementPoolService,
        World world,
        MapViewState mapViewState,
        ComponentManager componentManager,
        EventBus eventBus,
        ActionCatalog actionCatalog,
        ItemCatalog itemCatalog,
        TileRenderer tileRenderer,
        LabelRenderer labelRenderer,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer,
        MapCamera camera,
        ActionTargetingController actionTargeting,
        PlayerMovementController playerMovement,
        ContextMenuController contextMenuController,
        PackedComponentPool<ActionLockComponent> actionLockPool) : base(fontService, elementPoolService, labelRenderer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapViewState);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(actionCatalog);
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(tileRenderer);
        ArgumentNullException.ThrowIfNull(labelRenderer);
        ArgumentNullException.ThrowIfNull(spriteSheetService);
        ArgumentNullException.ThrowIfNull(spriteRenderer);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(actionTargeting);
        ArgumentNullException.ThrowIfNull(playerMovement);
        ArgumentNullException.ThrowIfNull(contextMenuController);
        ArgumentNullException.ThrowIfNull(actionLockPool);

        _world = world;
        _mapViewState = mapViewState;
        _transformPool = componentManager.GetDirectPool<TransformComponent>();
        _glyphPool = componentManager.GetDirectPool<GlyphComponent>();
        _spritePool = componentManager.GetDirectPool<SpriteComponent>();
        _nonBlockingPool = componentManager.GetMultiPool<NonBlockingComponent>();
        _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
        _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        _deadPool = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        _inventoryStacks = componentManager.IsRegistered<InventoryItemStackComponent>()
            ? componentManager.GetMultiPool<InventoryItemStackComponent>()
            : null;
        _lootedPool = componentManager.IsRegistered<LootedComponent>()
            ? componentManager.GetPackedPool<LootedComponent>()
            : null;
        _containerPool = componentManager.IsRegistered<ContainerComponent>()
            ? componentManager.GetPackedPool<ContainerComponent>()
            : null;
        _shopPool = componentManager.IsRegistered<ShopComponent>()
            ? componentManager.GetPackedPool<ShopComponent>()
            : null;
        _actionLockPool = actionLockPool;
        _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();
        _pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();
        _dodgingEntities = componentManager.GetPackedPool<DodgingComponent>();
        _actionCatalog = actionCatalog;
        _tileRenderer = tileRenderer;
        _labelRenderer = labelRenderer;
        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;

        _camera = camera;
        _actionTargeting = actionTargeting;
        _playerMovement = playerMovement;
        _contextMenuController = contextMenuController;
        _tintGrid = new MapTintGrid(componentManager, world.Map.Size, eventBus);
        _backgroundCache = new MapBackgroundCache(
            world,
            mapViewState,
            componentManager.GetDirectPool<BackgroundComponent>(),
            _camera);

        // Terrain is the one thing MapWindow draws that a rare, explicit event can invalidate
        // rather than per-frame change -- see MapTileLayerCache. Nothing publishes this today
        // (World.PlaceTerrainOnMap has only ever been called at population time), but subscribing
        // now is what keeps the first terrain-changing action from shipping with a stale-image bug.
        eventBus.Subscribe<TerrainChangedEvent>(_ => InvalidateTerrainCaches());

        _tileDepth = _world.Map.Size.Z;
    }

    /// <summary>One-time setup once this window's own content size is known -- font loading, camera/background-cache sizing, and the initial camera position.</summary>
    /// <remarks>Snaps the camera to the player's spawn position if it already exists at this point, otherwise resets the background cache to its empty state instead. In real gameplay the player already exists by the time this ever runs -- WorldSessionBootstrapper.Build spawns it before ShellBootstrapper.Build ever constructs a MapWindow -- but this still has to tolerate the player not existing, for a MapWindow built directly (e.g. tests) without going through that same sequence.</remarks>
    public override void Initialize()
    {
        base.Initialize();

        _mediumFont = FontService.GetFont(FontChrome.MapMediumFontSize);
        _largeFont = FontService.GetFont(FontChrome.MapLargeFontSize);
        _hugeFont = FontService.GetFont(FontChrome.MapHugeFontSize);
        _tinyFont = FontService.GetFont(FontChrome.MapTinyFontSize); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = FontService.GetFont(FontChrome.MapBadgeFontSize); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        _camera.Initialize(ContentSize);
        _backgroundCache.Resize();

        _lootBagSprite = SpriteManifest.TryGetFirst(LootBagSpriteName, out var lootBagSprite) ? lootBagSprite : null;

        SetCurrentMapLayer(_mapViewState.CurrentMapLayer);

        if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform))
        {
            SnapCameraToPlayer(playerTransform.Position);
            _camera.LastKnownPlayerPosition = playerTransform.Position;
        }
        else
        {
            InvalidateTerrainCaches();
        }
    }

    /// <summary>Per-frame camera-follow and hover-tracking.</summary>
    /// <remarks>
    /// Re-centers the camera only when the player's own position actually changed since last
    /// frame (not unconditionally every frame) and only while MapCamera.FollowsPlayer is true --
    /// a right-mouse drag decouples the camera until Home recouples it (see MapCamera's own doc
    /// comment).
    /// </remarks>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _actionTargeting.Tick();

        if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform) && playerTransform.Position != _camera.LastKnownPlayerPosition)
        {
            _camera.LastKnownPlayerPosition = playerTransform.Position;

            if (_camera.FollowsPlayer)
            {
                CenterCameraOn(playerTransform.Position);
            }
        }

        var mouseState = Mouse.GetState();
        UpdateHoveredTile(new Point(mouseState.X, mouseState.Y));

        // Constructed on first use rather than in Initialize: ElementPoolService only receives a
        // GraphicsDevice in GameLoop.LoadContent, which MonoGame runs AFTER Initialize -- so
        // checking there silently left this null forever and every frame fell back to the uncached
        // path. Staying null is still the correct outcome for a headless MapWindow built directly
        // in a test, which never gets a device at all.
        if (_terrainCache is null && ElementPoolService.GraphicsDevice is { } graphicsDevice)
        {
            _terrainCache = new MapTileLayerCache(graphicsDevice);
            _glowCache = new MapTileLayerCache(graphicsDevice);
        }

        if (_renderedGlowVersion != _tintGrid.Version)
        {
            _renderedGlowVersion = _tintGrid.Version;
            _glowCache?.Invalidate();
        }

        // Deliberately here and not in DrawContent: rendering into MapTileLayerCache's own texture
        // means swapping render targets, and by DrawContent GameLoop has already begun the shared
        // SpriteBatch pass that ElementPoolService's render-state stack owns. Update runs before
        // any of that, with no batch active. Runs after this method's camera-follow above, so a
        // frame that re-centres the camera rebuilds against the new scroll position rather than
        // blitting the old image once before catching up.
        _terrainCache?.EnsureRendered(
            ElementPoolService.SpriteBatch,
            _camera.TileColumns,
            _camera.TileRows,
            _camera.CurrentTileSize,
            DrawTerrainAndBackgroundsIntoCache);

        _glowCache?.EnsureRendered(
            ElementPoolService.SpriteBatch,
            _camera.TileColumns,
            _camera.TileRows,
            _camera.CurrentTileSize,
            DrawGlowOverlayIntoCache);
    }

    /// <summary>
    /// The single place both terrain-derived caches are invalidated together. They have identical
    /// dependencies -- scroll position, zoom, current map layer, and the terrain itself -- so
    /// invalidating them as a pair is what stops the background wash and the terrain image from
    /// ever disagreeing about which cells they describe.
    /// </summary>
    private void InvalidateTerrainCaches()
    {
        _backgroundCache.Reset();
        _terrainCache?.Invalidate();
        _glowCache?.Invalidate();
    }

    /// <summary>
    /// Delegates to ActionTargetingController.UpdateHoveredTile -- kept as a method on MapWindow
    /// (internal, not private) since MapWindowTests exercises it directly the same way it does
    /// Window.HandleHotkeys, simulating a mouse position without a real OS cursor.
    /// </summary>
    internal void UpdateHoveredTile(Point mousePosition) => _actionTargeting.UpdateHoveredTile(mousePosition, _contentState.AbsolutePosition);

    /// <summary>Read-only view of the armed ability's current hit-footprint -- see ActionTargetingController.HoveredFootprint.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _actionTargeting.HoveredFootprint;

    private void SnapCameraToPlayer(Vector3Int position)
    {
        SetCurrentMapLayer(position.Z);
        CenterCameraOn(position);
    }

    /// <summary>The single place [0, _tileDepth - 1] clamping happens for MapViewState.CurrentMapLayer -- shared by ChangeLayer, SnapToPlayer, and Initialize's own re-clamp against whatever depth this particular Map turns out to have.</summary>
    private void SetCurrentMapLayer(int layer)
    {
        _mapViewState.CurrentMapLayer = MathUtility.ClampInt(layer, 0, _tileDepth - 1);
    }

    /// <summary>Draws one frame of the map viewport: background, tile backgrounds, glyphs/sprites, glow overlay, then targeting/selection highlights, in that order.</summary>
    /// <remarks>
    /// Draw order is significant, not incidental -- each pass lands on top of the previous one with
    /// no depth buffer (SpriteSortMode.Deferred submits in call order), so highlights/glow have to
    /// come after the glyphs/sprites they're meant to sit on top of, and the flat background wash
    /// has to come first so everything else has something to draw over.
    ///
    /// The first two of those passes (tile backgrounds, then terrain) come from MapTileLayerCache as
    /// a single blit rather than being re-submitted tile-by-tile -- see that class for why terrain
    /// is the one thing here that can be cached as an image. DrawTerrainAndBackgroundsDirectly is
    /// the fallback for the frame or two before the cache's first render lands (and for a headless
    /// MapWindow that never got a GraphicsDevice), producing byte-identical output at the old cost.
    /// </remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, _camera.TileColumns * _camera.CurrentTileSize.X, _camera.TileRows * _camera.CurrentTileSize.Y), MapBackgroundColor);

        if (_terrainCache?.Draw(spriteBatch, -_camera.RenderPixelOffset) != true)
        {
            DrawTerrainAndBackgroundsDirectly(spriteBatch, _camera.RenderPixelOffset);
        }

        DrawOccupants(spriteBatch, unitRectangle);

        if (_glowCache?.Draw(spriteBatch, -_camera.RenderPixelOffset) != true)
        {
            DrawGlowOverlay(spriteBatch, unitRectangle, _camera.RenderPixelOffset);
        }

        DrawTargetingHighlights(spriteBatch, unitRectangle, gameTime);

        if (_mapViewState.InspectionMode == InspectionMode.Detail)
        {
            DrawFollowedEntityHighlight(spriteBatch, unitRectangle);
        }
        else
        {
            DrawSelectedTileHighlight(spriteBatch, unitRectangle);
        }
    }

    /// <summary>
    /// StatusEffectAuraSourceComponent's glow (see MapTintGrid), drawn as a translucent overlay
    /// on top of terrain/occupant sprites rather than blended into the background color
    /// underneath them. Blending it into the background (the old approach) only ever showed
    /// through a small, mostly-transparent glyph -- a full-tile opaque sprite hides an
    /// underlying background color completely, which silently broke the glow the moment
    /// terrain/entities started rendering as sprites. Drawing the same tint as its own
    /// translucent rect on top means it shows over a sprite exactly the way it used to show
    /// over a flat background color.
    /// </summary>
    /// <summary>Renders the glow overlay at whole-tile positions with no sub-tile offset, for MapTileLayerCache to capture -- the caller applies the offset once when blitting.</summary>
    private void DrawGlowOverlayIntoCache(SpriteBatch spriteBatch) =>
        DrawGlowOverlay(spriteBatch, ElementPoolService.UnitRectangle, Vector2.Zero);

    private void DrawGlowOverlay(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 pixelOffset)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;

        for (var columnIndex = 0; columnIndex < _camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _camera.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _camera.CurrentScrollPosition.X;
                var mapNodeY = rowIndex + _camera.CurrentScrollPosition.Y;

                if (!_world.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)) || !_tintGrid.TryGetTint(mapNodeX, mapNodeY, currentMapLayer, out var tint))
                {
                    continue;
                }

                var tileOrigin = new Vector2(columnIndex * _camera.CurrentTileSize.X, rowIndex * _camera.CurrentTileSize.Y) - pixelOffset;
                var destination = new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
                spriteBatch.Draw(unitRectangle, destination, tint.Color * tint.Factor * GlowOpacityMultiplier);
            }
        }
    }

    /// <summary>Delegates to MapCamera.TileOrigin -- kept as a same-signature method here rather than inlined at every call site below.</summary>
    private Vector2 TileOrigin(int columnIndex, int rowIndex) => _camera.TileOrigin(columnIndex, rowIndex);

    /// <summary>
    /// Every tile the currently-armed ability could be aimed at (see MapViewState.TargetableTiles,
    /// computed once at arm time) -- a white border + 50% white mask for "targetable, not
    /// currently hovered," a red border + 50% red mask for whichever of those tiles the armed
    /// shape's hover-resolved footprint (see ActionTargetingController.HoveredFootprint,
    /// recomputed every Update) actually covers right now. A separate, independent draw call
    /// from DrawSelectedTileHighlight below -- the two are conceptually distinct (ability
    /// targeting vs. the inspector's click-to-select), even though both now share the same
    /// border-plus-mask technique (see DrawMaskedTileHighlight).
    ///
    /// A second pass then draws whatever's left of HoveredFootprint that TargetableTiles didn't
    /// already cover -- needed for Burst: TargetableTiles is capped strictly at the action's own
    /// Range (ActionTargetingController.ComputeTargetableTiles' reachable-area scatter), but
    /// TargetShapeResolver.ResolveBurst's actual hit footprint is AreaSize-radius around the
    /// anchor tile once that anchor passes the Range check -- i.e. the real splash can (and
    /// often does) reach past Range even though the anchor itself never could. Without this pass
    /// the highlight understated what the ability actually hits (confirmed in-game: entities
    /// outside the drawn splat still took the effect). Safe to always run: ResolveBurst (and
    /// every other shape) only ever populates HoveredFootprint from an in-range anchor to begin
    /// with, so this never draws a tile that wouldn't actually be hit; it also naturally no-ops
    /// for every shape whose own footprint can't exceed Range in the first place (SingleTarget/
    /// Line/Cone), since TargetableTiles already covers all of those.
    ///
    /// Once a Delayed ability is actually queued, Disarm already clears TargetableTiles (there's
    /// nothing left to aim), but the player benefits from still seeing exactly which tiles are
    /// about to be hit once the windup ends -- so this falls back to highlighting
    /// ActionTargetingController.PendingDelayedActionTargetTiles (the already-resolved,
    /// locked-in footprint) in the same dark green used for a confirmed hover target, for as long
    /// as that pending action exists. Separately (and unconditionally, every frame, regardless of
    /// the player's own armed/pending state above), every OTHER entity's own in-flight Delayed
    /// windup is telegraphed too, in red or yellow depending on whether it's Dodgeable -- see
    /// CombatTargetPalette and ActionTargetingController.AllPendingDelayedActionTargets. This is
    /// the "delayed actions keep the target shape drawn on the map... until they activate" half of
    /// Combat Overhaul: Dodge (TODO.md).
    /// </summary>
    private void DrawTargetingHighlights(SpriteBatch spriteBatch, Texture2D unitRectangle, GameTime gameTime)
    {
        var elapsedSimulationFrames = (float)(gameTime.ElapsedGameTime.TotalSeconds * GameTiming.FramesPerSecond);
        _activeChargingEntityIdsBuffer.Clear();

        if (_mapViewState.TargetableTiles is { Count: > 0 } targetableTiles)
        {
            foreach (var tile in targetableTiles)
            {
                var borderColor = _actionTargeting.HoveredFootprintContains(tile) ? CombatTargetPalette.PlayerTargetColor : CombatTargetPalette.PlayerArmColor;
                DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor);
            }

            foreach (var tile in _actionTargeting.HoveredFootprint)
            {
                if (!targetableTiles.Contains(tile))
                {
                    DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor);
                }
            }
        }
        else if (_actionTargeting.PendingDelayedActionTargetTiles is { } pendingTargetTiles)
        {
            // The player is always eligible regardless of tier -- it's the camera anchor, always
            // relevant -- so it's tracked here unconditionally rather than through the Local-tier
            // check below, which only applies to every OTHER entity.
            _activeChargingEntityIdsBuffer.Add(_world.PlayerEntityId);
            var fillFraction = TryGetChargeFraction(_world.PlayerEntityId, elapsedSimulationFrames);
            foreach (var tile in pendingTargetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor, fillFraction);
            }
        }

        // Every OTHER entity's own Delayed-action telegraph -- red (undodgeable) or yellow
        // (Dodgeable), per Combat Overhaul: Dodge. The player's own is already drawn above (dark
        // green, "player target"), so it's skipped here to avoid drawing it twice.
        // AllPendingDelayedActionTargets itself already scopes this to Local processing tier (see
        // that method's own doc comment) -- MapWindow doesn't need its own second tier check here.
        foreach (var (entityId, targetTiles, isDodgeable) in _actionTargeting.AllPendingDelayedActionTargets())
        {
            if (entityId == _world.PlayerEntityId)
            {
                continue;
            }

            _activeChargingEntityIdsBuffer.Add(entityId);

            var borderColor = isDodgeable ? CombatTargetPalette.EnemyDodgeableColor : CombatTargetPalette.EnemyUndodgeableColor;
            var fillFraction = TryGetChargeFraction(entityId, elapsedSimulationFrames);
            foreach (var tile in targetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor, fillFraction);
            }
        }

        PruneChargeFillSmoothingState();

        DrawDodgeDirectionalHints(spriteBatch);
    }

    /// <summary>WASD's own screen-direction offsets from the player -- matches ActionTargetingController.TryClaimDodgeDirectionalKey's identical mapping.</summary>
    private static readonly (Vector3Int Offset, string Label)[] DodgeDirectionalHints =
    [
        (new Vector3Int(0, -1, 0), "W"),
        (new Vector3Int(0, 1, 0), "S"),
        (new Vector3Int(-1, 0, 0), "A"),
        (new Vector3Int(1, 0, 0), "D"),
    ];

    /// <summary>
    /// While Dodge is armed, labels each of its four cardinal reachable tiles with the WASD key
    /// that confirms toward it -- the only action whose confirm can come from a movement key
    /// instead of its own hotkey (ActionTargetingController.TryClaimDodgeDirectionalKey), so it
    /// needs its own affordance the player wouldn't otherwise expect. Diagonal neighbors are still
    /// reachable (click, or same-key-then-click), just not via a single key -- no hint drawn there.
    /// </summary>
    private void DrawDodgeDirectionalHints(SpriteBatch spriteBatch)
    {
        if (_mapViewState.ArmedActionId != DodgeAction.Id || !_transformPool.TryGetReadonly(_world.PlayerEntityId, out var transform))
        {
            return;
        }

        foreach (var (offset, label) in DodgeDirectionalHints)
        {
            var tile = transform.Position + offset;
            if (!TryGetTileRectangle(tile.X, tile.Y, out var tileRectangle))
            {
                continue;
            }

            _labelRenderer.DrawCentered(spriteBatch, _badgeFont, label, new Vector2(tileRectangle.X, tileRectangle.Y), new Vector2(tileRectangle.Width, tileRectangle.Height), Color.White, outline: true);
        }
    }

    private void DrawSelectedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        DrawSelectedTileGlow(spriteBatch, unitRectangle, selectedPosition.X, selectedPosition.Y);
    }

    /// <summary>
    /// Detail/Admin inspection's own highlight -- tracks the followed entity's live
    /// TransformComponent.Position every frame instead of a frozen tile position (see
    /// DrawSelectedTileHighlight, Basic mode's equivalent), so the highlight moves with the
    /// entity as it walks. Highlights every tile of the entity's own footprint (TransformComponent.
    /// Size), not just its origin tile -- a multi-tile (Huge) entity should read as fully
    /// selected, the same footprint DrawPrimaryOccupant itself draws the entity's sprite across.
    /// DrawMaskedTileHighlight already no-ops cleanly per-tile when a given tile falls outside
    /// the camera's currently visible viewport, so an entity that walks off-screen (in whole or
    /// in part) simply stops drawing a highlight for whichever tiles are off-screen, rather than
    /// throwing or drawing garbage -- no extra guard needed here for that case.
    /// </summary>
    private void DrawFollowedEntityHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var entityId = _mapViewState.InspectedEntityId;
        if (entityId == -1 || !_transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        for (var offsetX = 0; offsetX < transform.Size.X; offsetX++)
        {
            for (var offsetY = 0; offsetY < transform.Size.Y; offsetY++)
            {
                DrawSelectedTileGlow(spriteBatch, unitRectangle, transform.Position.X + offsetX, transform.Position.Y + offsetY);
            }
        }
    }

    /// <summary>
    /// A uniform translucent borderColor wash (TargetSelectionMaskAlpha alpha) over the whole
    /// tile -- shared by the inspector's single-tile Gold selection and DrawTargetingHighlights'
    /// per-tile ability-targeting colors. Drawn after DrawGlyphs/DrawGlowOverlay (not before,
    /// like the tile backgrounds) so it lands on top of whatever's actually on the tile --
    /// terrain/occupant sprite, glyph, or glow -- rather than getting hidden underneath an opaque
    /// sprite the way this used to. The whole tile is translucent (not just an inset "mask" with
    /// a solid opaque border ring, the earlier technique) specifically so the sprite stays
    /// visible through the border too, not just the interior.
    /// </summary>
    private void DrawMaskedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color borderColor)
    {
        if (!TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        spriteBatch.Draw(unitRectangle, tileRectangle, borderColor * TargetSelectionMaskAlpha);
    }

    /// <summary>
    /// 0 at charge start -&gt; 1 at activation, inverted from ActionLockContent's own remaining/
    /// total framing, per the Enemy Attack Indicator TODO's own wording. Guards
    /// CurrentLockTotalFrames == 0 (no lock) the same way ActionLockContent.Update does.
    /// </summary>
    /// <remarks>
    /// This does NOT read CurrentLockFramesRemaining -- it deliberately never did smoothly, and
    /// briefly did via a "climb toward the raw stepped value, never exceed it" design that turned
    /// out to hide a real bug: DelayedActionSystem resolves and removes
    /// PendingDelayedActionComponent in the exact same tiered visit that finally observes
    /// CurrentLockFramesRemaining == 0, so the raw value is NEVER actually observable at 0 --
    /// the last frame this entity is ever seen pending, it's frozen at whatever
    /// CurrentLockFramesRemaining held after the second-to-last decrement (up to
    /// ActionLockSystem's own StripeCountValue - 1 frames short of the true end), then the entity
    /// simply vanishes. Confirmed live: every indicator completed at roughly 80-90% and never
    /// higher. See TrackChargeElapsedFraction below for the fix (elapsed real time since this
    /// charge started, not the stepped countdown at all).
    /// </remarks>
    private float TryGetChargeFraction(int entityId, float elapsedSimulationFrames)
    {
        if (!_actionLockPool.TryGetReadonly(entityId, out var actionLock) || actionLock.CurrentLockTotalFrames <= 0)
        {
            return 0f;
        }

        return TrackChargeElapsedFraction(entityId, actionLock.CurrentLockTotalFrames, elapsedSimulationFrames);
    }

    /// <summary>
    /// Fraction of CurrentLockTotalFrames elapsed since this specific charge was first observed,
    /// accumulated from real elapsed time (elapsedSimulationFrames, via GameTiming.FramesPerSecond
    /// -- robust to any Draw/Update cadence mismatch) rather than derived from
    /// ActionLockComponent's own stepped countdown at all -- see TryGetChargeFraction's own remarks
    /// for why reading that countdown directly caps the fill short of 100%. Reaching exactly 1
    /// right at totalFrames means this can show "done" a few frames before DelayedActionSystem's
    /// own next tiered visit actually resolves the effect (the same up-to-StripeCountValue-1-frame
    /// slop already inherent to the countdown this mirrors) -- a brief, barely-perceptible "full and
    /// waiting" instead of a perpetual shortfall. Safe to never reconcile against the real
    /// CurrentLockFramesRemaining because AllPendingDelayedActionTargets/
    /// PendingDelayedActionTargetTiles already scope every caller of this method to Local tier --
    /// nothing slower-than-nominal (see the old TestDummyBlueprint mis-tiering bug this codebase
    /// already hit once) ever reaches this code to begin with.
    /// </summary>
    private float TrackChargeElapsedFraction(int entityId, ushort totalFrames, float elapsedSimulationFrames)
    {
        if (!_chargeFillElapsedFrames.TryGetValue(entityId, out var elapsedFrames))
        {
            // First observation of this entity's charge always starts counting from 0, even if
            // the real windup is already partway through -- correct for the overwhelmingly common
            // case (a Local-tier entity's charge starts the same frame PendingDelayedActionComponent
            // is created, so tracking begins at the very first Draw call after that, effectively
            // elapsed 0 already). The one known gap: an entity that crosses INTO Local tier
            // mid-charge (player closing distance on an already-charging, previously-untracked
            // entity) would restart its visible fill from 0% instead of resuming from its true
            // progress -- accepted as narrow and self-correcting (one full totalFrames-long fill
            // later, it reads correctly), not worth threading the real elapsed time through for.
            elapsedFrames = 0f;
        }

        elapsedFrames = Math.Min(elapsedFrames + elapsedSimulationFrames, totalFrames);
        _chargeFillElapsedFrames[entityId] = elapsedFrames;
        return elapsedFrames / totalFrames;
    }

    /// <summary>
    /// Drops smoothing state for any entity DrawTargetingHighlights didn't see charging this
    /// frame -- otherwise every entity that ever charged a Delayed action during the session
    /// would keep an entry forever. _activeChargingEntityIdsBuffer is DrawTargetingHighlights'
    /// own already-built set (player included when charging, every Local-tier-or-closer other
    /// entity), not a second pool scan.
    /// </summary>
    /// <remarks>
    /// O(D + A) (D = _chargeFillElapsedFrames.Count, A = _activeChargingEntityIdsBuffer.Count,
    /// both already bounded to Local tier by the caller) via HashSet.Contains, not O(D * A) -- an
    /// earlier version linear-scanned a plain list per stale-candidate, which is fine for a
    /// couple of entities but becomes the exact "unstriped, full-population scan every single
    /// Draw call" anti-pattern TestCombatBehaviorSystem's own doc comment already flags as this
    /// codebase's prior ~2fps incident, once enough entities across the map are simultaneously
    /// mid-windup at once.
    /// </remarks>
    private void PruneChargeFillSmoothingState()
    {
        if (_chargeFillElapsedFrames.Count == 0)
        {
            return;
        }

        _staleChargeFillEntityIdsBuffer.Clear();
        foreach (var entityId in _chargeFillElapsedFrames.Keys)
        {
            if (!_activeChargingEntityIdsBuffer.Contains(entityId))
            {
                _staleChargeFillEntityIdsBuffer.Add(entityId);
            }
        }

        foreach (var staleEntityId in _staleChargeFillEntityIdsBuffer)
        {
            _chargeFillElapsedFrames.Remove(staleEntityId);
        }
    }

    /// <summary>
    /// A charging Delayed action's own per-tile telegraph -- an always-visible, dim full-tile
    /// backdrop (ChargeBackdropAlphaFraction of DrawMaskedTileHighlight's own alpha, so a
    /// multi-tile target shape still reads as one coherent zone from the first frame of the
    /// windup) plus a brighter bottom-up fill on top that grows with fillFraction, reaching
    /// DrawMaskedTileHighlight's own full alpha -- and its own full-tile coverage -- exactly as
    /// the action activates. See PLAN-charge-attack-fill-indicator.md.
    /// </summary>
    private void DrawChargeFillHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color fillColor, float fillFraction)
    {
        if (!TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        spriteBatch.Draw(unitRectangle, tileRectangle, fillColor * TargetSelectionMaskAlpha * ChargeBackdropAlphaFraction);
        TileFillRenderer.DrawBottomUpFill(spriteBatch, unitRectangle, tileRectangle, fillFraction, fillColor * TargetSelectionMaskAlpha);
    }

    /// <summary>The inspector's own "this tile is selected" highlight -- a light-blue interior-fade glow (see GridSquareRenderer's own use of the same GlowMode.InteriorFade for inventory cells, so the two read as the same visual language) rather than DrawMaskedTileHighlight's flat wash, so terrain/sprites underneath stay fully legible through the ring gaps instead of being tinted.</summary>
    private void DrawSelectedTileGlow(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY)
    {
        if (!TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        GlowRenderer.Draw(spriteBatch, unitRectangle, tileRectangle, WindowPalette.Selected, GlowMode.InteriorFade);
    }

    /// <summary>The on-screen rectangle for a map-node tile, or false if it's currently scrolled outside the camera's visible viewport -- shared by DrawMaskedTileHighlight and DrawSelectedTileGlow so both no-op identically for an off-screen tile instead of throwing or drawing garbage.</summary>
    private bool TryGetTileRectangle(int mapNodeX, int mapNodeY, out Rectangle tileRectangle)
    {
        var column = mapNodeX - _camera.CurrentScrollPosition.X;
        var row = mapNodeY - _camera.CurrentScrollPosition.Y;

        if (column < 0 || column >= _camera.TileColumns || row < 0 || row >= _camera.TileRows)
        {
            tileRectangle = Rectangle.Empty;
            return false;
        }

        var origin = TileOrigin(column, row);
        tileRectangle = new Rectangle((int)origin.X, (int)origin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
        return true;
    }

    /// <summary>Switches the single MapLayer this window renders, by delta layers.</summary>
    /// <remarks>
    /// Stored on MapViewState.CurrentMapLayer, not locally, so SelectionWindowContent can scope
    /// the inspector to the same layer this window is actually showing. Background depends on
    /// the current layer's terrain (see MapBackgroundCache), so the cache must be rebuilt on
    /// every change, the same as a zoom-level change. Called from OnHotkeysAction (Page Up/Down).
    /// </remarks>
    /// <param name="delta">Layers to move by -- positive moves up, negative moves down (clamped to the map's own depth by SetCurrentMapLayer).</param>
    public void ChangeLayer(int delta)
    {
        SetCurrentMapLayer(_mapViewState.CurrentMapLayer + delta);
        InvalidateTerrainCaches();
    }

    /// <summary>
    /// Renders the tile backgrounds and terrain at whole-tile positions with no sub-tile offset,
    /// for MapTileLayerCache to capture into its texture -- the caller applies the offset once when
    /// blitting that texture instead of every tile applying it individually.
    /// </summary>
    /// <remarks>
    /// Terrain is drawn as its own full pass, ahead of every occupant, and that ordering is
    /// load-bearing rather than incidental: a multi-tile entity's sprite is drawn once from its
    /// origin tile covering its whole footprint, so a neighbouring tile's terrain draw -- a later
    /// call, and SpriteSortMode.Deferred submits in call order with no depth buffer -- would
    /// otherwise land on top of part of that footprint. Now that terrain lives in its own texture
    /// blitted before any occupant, that separation is structural rather than something the loop
    /// order has to keep getting right.
    /// </remarks>
    private void DrawTerrainAndBackgroundsIntoCache(SpriteBatch spriteBatch) =>
        DrawTerrainAndBackgroundsDirectly(spriteBatch, Vector2.Zero);

    /// <summary>The uncached path: the same backgrounds-then-terrain output MapTileLayerCache captures, drawn straight to the screen at the given sub-tile offset. Used to render into the cache (offset zero) and as DrawContent's fallback before the cache's first render lands.</summary>
    private void DrawTerrainAndBackgroundsDirectly(SpriteBatch spriteBatch, Vector2 pixelOffset)
    {
        var terrainLayer = Map.TerrainLayerFor(_mapViewState.CurrentMapLayer);

        _tileRenderer.DrawBackgrounds(spriteBatch, ElementPoolService.UnitRectangle, _backgroundCache.Colors, _camera.TileColumns, _camera.TileRows, _camera.CurrentTileSize, pixelOffset);

        for (var columnIndex = 0; columnIndex < _camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _camera.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _camera.CurrentScrollPosition.X;
                var mapNodeY = rowIndex + _camera.CurrentScrollPosition.Y;

                if (!_world.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)))
                {
                    continue;
                }

                var tileOrigin = new Vector2(columnIndex * _camera.CurrentTileSize.X, rowIndex * _camera.CurrentTileSize.Y) - pixelOffset;
                DrawTerrainGlyph(spriteBatch, terrainLayer, mapNodeX, mapNodeY, tileOrigin);
            }
        }
    }

    /// <summary>Every occupant of the visible grid, drawn on top of MapTileLayerCache's already-blitted terrain -- corpses, the Tiny sub-grid, the Blocking occupant, Phasing overlays, and the tile's own up/down layer badges.</summary>
    /// <remarks>
    /// Column-outer, row-inner. Map's per-cell arrays are indexed X-fastest (see
    /// Vector3Int.FlatIndex), so this nesting strides the flat index by a whole map row per
    /// iteration and a row-major walk looks like it should read far better. Measured within a
    /// single frame, against the same warm cache, it makes no difference: the visible grid touches
    /// only a few dozen rows of each array and stays resident either way, and an A/B that walked
    /// both orders per frame showed whichever ran SECOND winning by the same margin regardless of
    /// which one it was. Left as-is rather than reordered, since changing it would resettle the
    /// submission order of overlapping occupants (SpriteSortMode.Deferred draws in call order,
    /// with no depth buffer) for no measured gain.
    /// </remarks>
    private void DrawOccupants(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;

        // Which Map.GetOccupiedLayerMask bits count as "above" and "below" the layer being drawn.
        // Depends only on currentMapLayer, so it's computed once per frame here rather than
        // re-derived per tile inside DrawLayerBadges.
        var allLayersMask = (1 << _tileDepth) - 1;
        var higherLayerMask = allLayersMask & ~((1 << (currentMapLayer + 1)) - 1);
        var lowerLayerMask = (1 << currentMapLayer) - 1;

        for (var columnIndex = 0; columnIndex < _camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _camera.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _camera.CurrentScrollPosition.X;
                var mapNodeY = rowIndex + _camera.CurrentScrollPosition.Y;

                if (!_world.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)))
                {
                    continue;
                }

                var tileOrigin = TileOrigin(columnIndex, rowIndex);
                var blockingEntityId = _world.Map.GetBlockingEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
                var occupantsHere = _world.Map.GetOccupantEntityIdSpanAt(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));

                DrawUnderlayOccupants(spriteBatch, occupantsHere, blockingEntityId, mapNodeX, mapNodeY, tileOrigin);
                DrawPrimaryOccupant(spriteBatch, unitRectangle, blockingEntityId, mapNodeX, mapNodeY, columnIndex, rowIndex);
                DrawPhasingOverlay(spriteBatch, tileOrigin);
                DrawLayerBadges(spriteBatch, higherLayerMask, lowerLayerMask, mapNodeX, mapNodeY, tileOrigin);
            }
        }
    }

    /// <summary>Draws entityId's sprite if it has one, else falls back to its glyph -- the one place that decides sprite-vs-glyph, shared by every per-tile visual draw below. Returns whether anything was actually drawn. A corpse (DeadComponent) draws with a flat Color.Gray tint instead of its normal color -- a color-multiply override, not a true desaturation shader (no shader/Effect infrastructure exists here). Delegates the actual draw to SpriteOrGlyphRenderer, shared with Folder/inventory item cells -- this method's only job is resolving entityId's own sprite/glyph/dead-tint inputs.</summary>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, float alphaMultiplier = 1f)
    {
        var isDead = _deadPool?.Has(entityId) == true;

        // The glyph pool is only consulted when there is no sprite. SpriteOrGlyphRenderer returns
        // on the sprite branch without ever looking at the glyph, so resolving both unconditionally
        // (as this used to) meant one wasted scattered read into an entity-indexed array for every
        // sprite-backed entity, every frame -- and every entity that draws at all is on this path.
        if (_spritePool.TryGetReadonly(entityId, out var spriteComponent))
        {
            return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, spriteComponent, font, string.Empty, Color.White, footprintTopLeft, footprintSize, isDead ? Color.Gray : Color.White, alphaMultiplier, outline: true);
        }

        if (!_glyphPool.TryGetReadonly(entityId, out var glyphComponent))
        {
            return false;
        }

        return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, null, font, glyphComponent.Glyph, isDead ? Color.Gray : glyphComponent.GlyphColor, footprintTopLeft, footprintSize, Color.White, alphaMultiplier, outline: true);
    }

    /// <summary>
    /// Every non-Blocking occupant that draws UNDER the tile's Blocking occupant -- corpses at
    /// their full footprint, then the Tiny 3x3 sub-grid -- in one walk of the occupant list.
    /// Phasing occupants are collected here but drawn afterwards by DrawPhasingOverlay, since
    /// they have to land on top of the Blocking occupant, not beneath it.
    /// </summary>
    /// <remarks>
    /// One pass, not three. This used to be DrawCorpses, DrawTinyGrid and DrawPhasingGlyphs each
    /// walking the same list independently, and each independently re-deriving the same two facts
    /// about every occupant: whether it is Blocking, and its combined NonBlockingKind. Both are
    /// scattered reads into entity-indexed arrays sized to this game's whole entity population, so
    /// a single occupant cost roughly seven of them to answer two questions. Computing each once
    /// and dispatching on the result cuts that to about three.
    ///
    /// "Is this the Blocking occupant" is answered by comparing against the tile's own Blocking
    /// entity id rather than by calling World.IsBlocking. That is cheaper (a register compare
    /// instead of two multi-pool lookups) and also more correct for what the check is actually
    /// for: these skips exist to avoid drawing the entity DrawPrimaryOccupant already drew, and
    /// DrawPrimaryOccupant sources that entity from the map's Blocking index, not from IsBlocking.
    /// The two can disagree if anything ever adds a NonBlockingComponent without routing through
    /// World.ConvertToNonBlocking (see World.RemoveFootprint's own note on that gap), and when
    /// they do, the map index is the one that decides what actually got drawn.
    /// </remarks>
    private void DrawUnderlayOccupants(SpriteBatch spriteBatch, ReadOnlySpan<int> occupants, int blockingEntityId, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        _phasingOccupantsBuffer.Clear();

        if (occupants.IsEmpty)
        {
            return;
        }

        var subCellSize = new Point(_camera.CurrentTileSize.X / TinyGridDimension, _camera.CurrentTileSize.Y / TinyGridDimension);
        var tinyDrawnCount = 0;

        foreach (var entityId in occupants)
        {
            var isBlockingHere = entityId == blockingEntityId;
            var kind = NonBlockingQueries.CombinedKind(_nonBlockingPool, entityId);

            if (!isBlockingHere && (kind & NonBlockingKind.Phasing) != 0)
            {
                _phasingOccupantsBuffer.Add(entityId);
            }

            if (!isBlockingHere && (kind & NonBlockingKind.Tiny) != 0 && tinyDrawnCount < MaxTinyEntitiesDrawn)
            {
                var subColumn = tinyDrawnCount % TinyGridDimension;
                var subRow = tinyDrawnCount / TinyGridDimension;
                var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

                if (TryDrawEntityVisual(spriteBatch, entityId, _tinyFont, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y)))
                {
                    tinyDrawnCount++;
                }

                continue;
            }

            // A corpse with no NonBlockingKind flag at all -- one that used to be Blocking and no
            // longer holds that slot (see DeathSystem / World.ConvertToNonBlocking). A corpse that
            // was ALREADY non-Blocking when it died (a Phasing Ghost, a Tiny creature) is drawn by
            // whichever branch above matches its Kind instead, greyed by TryDrawEntityVisual's own
            // DeadComponent check either way. Deliberately not gated on isBlockingHere, matching
            // the behaviour this replaced.
            if ((kind & (NonBlockingKind.Tiny | NonBlockingKind.Phasing)) == 0 &&
                _deadPool?.Has(entityId) == true &&
                _transformPool.TryGetReadonly(entityId, out var corpseTransform) &&
                corpseTransform.Position.X == mapNodeX && corpseTransform.Position.Y == mapNodeY)
            {
                var footprintSize = new Vector2(corpseTransform.Size.X * _camera.CurrentTileSize.X, corpseTransform.Size.Y * _camera.CurrentTileSize.Y);

                TryDrawEntityVisual(spriteBatch, entityId, FontForSize(corpseTransform.Size.X), tileOrigin, footprintSize);
                DrawLootBagBadgeIfCarryingItems(spriteBatch, entityId, tileOrigin, footprintSize);
            }
        }
    }

    /// <summary>
    /// Every Phasing occupant DrawUnderlayOccupants collected for this tile, at 50% alpha and
    /// stacked -- SpriteBatchRenderer already begins with BlendState.AlphaBlend. Drawn after
    /// DrawPrimaryOccupant rather than with the rest of the occupant walk, because a Phasing
    /// entity sharing a tile with a Blocking one has to read as translucently in front of it; an
    /// opaque full-tile sprite drawn afterwards would hide it completely.
    /// </summary>
    private void DrawPhasingOverlay(SpriteBatch spriteBatch, Vector2 tileOrigin)
    {
        foreach (var entityId in _phasingOccupantsBuffer)
        {
            if (!_transformPool.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            var footprintSize = new Vector2(transformComponent.Size.X * _camera.CurrentTileSize.X, transformComponent.Size.Y * _camera.CurrentTileSize.Y);

            TryDrawEntityVisual(spriteBatch, entityId, FontForSize(transformComponent.Size.X), tileOrigin, footprintSize, alphaMultiplier: 0.5f);
        }
    }

    private void DrawTerrainGlyph(SpriteBatch spriteBatch, TerrainLayer? terrainLayer, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        if (terrainLayer is not { } layer)
        {
            return;
        }

        var terrainEntityId = _world.Map.GetTerrainEntityId(mapNodeX, mapNodeY, layer);
        if (terrainEntityId == -1)
        {
            return;
        }

        var footprintSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y); // Terrain is always 1x1.
        TryDrawEntityVisual(spriteBatch, terrainEntityId, _mediumFont, tileOrigin, footprintSize);
    }


    /// <summary>entityId is the tile's Blocking occupant, already read by the caller -- DrawUnderlayOccupants needs the same value to decide which occupants DrawPrimaryOccupant is about to cover, so it is fetched once per tile and passed to both rather than read twice.</summary>
    private void DrawPrimaryOccupant(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, int mapNodeX, int mapNodeY, int columnIndex, int rowIndex)
    {
        if (entityId == -1)
        {
            return;
        }

        if (!_transformPool.TryGetReadonly(entityId, out var transformComponent))
        {
            return;
        }

        // Multi-tile glyph fix: only draw from the entity's top-left origin tile
        // to avoid drawing it once per occupied tile.
        if (transformComponent.Position.X != mapNodeX || transformComponent.Position.Y != mapNodeY)
        {
            return;
        }

        // The footprint is Size tiles wide/tall, not 1 -- a 3x3 Huge entity's glyph must
        // center across all three tiles it actually occupies, not just the origin tile.
        var footprintTopLeft = TileOrigin(columnIndex, rowIndex);
        var footprintSize = new Vector2(transformComponent.Size.X * _camera.CurrentTileSize.X, transformComponent.Size.Y * _camera.CurrentTileSize.Y);

        TryDrawEntityVisual(spriteBatch, entityId, FontForSize(transformComponent.Size.X), footprintTopLeft, footprintSize);
        DrawEntityIcons(spriteBatch, unitRectangle, entityId, footprintTopLeft, footprintSize);

        // Unlike DrawEntityIcons (health bar/loot badge), the charging badge must show for the
        // player too -- drawn here, directly, rather than inside DrawEntityIcons' own early
        // player-skip guard.
        DrawChargingBadge(spriteBatch, entityId, footprintTopLeft, footprintSize);

        // Dodging is a temporary, brief immunity window (Combat Overhaul: Dodge) -- an inner-fade
        // glow framing the entity's own footprint on top of its sprite (the same GlowMode.InteriorFade
        // ring technique DrawSelectedTileGlow already uses), not a sprite-opacity fade: a translucent
        // sprite read as the entity vanishing outright rather than as a status cue (confirmed live).
        // GlowRenderer's own default rings top out at 50% alpha on the outermost ring, fading to 10%
        // on the innermost -- tuned for a tile-selection cue looked at at leisure, not a fast, brief
        // (as little as 0.5s at low Dexterity) combat status cue -- DodgingGlowAlphaMultiplier boosts
        // the outer rings toward fully opaque so the fade is still visible at a glance, confirmed live
        // as too subtle to notice at the un-boosted default.
        if (_dodgingEntities.Has(entityId))
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, new Rectangle((int)footprintTopLeft.X, (int)footprintTopLeft.Y, (int)footprintSize.X, (int)footprintSize.Y), DodgingGlowColor, GlowMode.InteriorFade, DodgingGlowAlphaMultiplier);
        }
    }


    /// <summary>
    /// Shared by DrawCorpses (a dead creature) and DrawEntityIcons (a live, still-Blocking
    /// container -- see ContainerComponent's own doc comment: lootable while alive, unlike a
    /// corpse) -- draws the LootBag-Red badge only when the entity actually carries items, at
    /// full color if its loot window has never been opened, or grey-tinted once it has (the same
    /// LootedComponent-driven cue either way, regardless of which path called this).
    /// </summary>
    private void DrawLootBagBadgeIfCarryingItems(SpriteBatch spriteBatch, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (_inventoryStacks?.CountForEntity(entityId) > 0)
        {
            var alreadyLooted = _lootedPool?.Has(entityId) == true;
            DrawLootBagBadge(spriteBatch, footprintTopLeft, footprintSize, alreadyLooted ? Color.Gray : Color.White);
        }
    }

    /// <summary>
    /// Small, single-tile-sized badge anchored to the top-right corner of the entity's own full
    /// footprint -- footprintTopLeft/footprintSize, not just the origin tile's own tileOrigin, so
    /// a multi-tile (Huge) corpse gets its badge on its actual top-right tile rather than the
    /// top-right corner of just its first (origin) tile. tint is Color.White (unlooted) or
    /// Color.Gray (already looted) -- see DrawCorpses' own doc comment.
    /// </summary>
    private void DrawLootBagBadge(SpriteBatch spriteBatch, Vector2 footprintTopLeft, Vector2 footprintSize, Color tint)
    {
        if (_lootBagSprite is not { } lootBagSprite)
        {
            return;
        }

        var badgeSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y) * LootBagBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + footprintSize.X - badgeSize.X, footprintTopLeft.Y);

        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, lootBagSprite, _badgeFont, string.Empty, tint, badgePosition, badgeSize, tint, outline: true);
    }

    /// <summary>
    /// While entityId is mid-windup on a Delayed action (PendingDelayedActionComponent), draws
    /// that action's own sprite/glyph as a small badge centered above its footprint -- "put that
    /// action/item's sprite as a badge above their sprite on the map" (Combat Overhaul: Dodge,
    /// TODO.md). Positioned a full badge-height above footprintTopLeft.Y (not flush with it) so it
    /// never collides with DrawHealthBar's own bar, which sits inside the footprint's top edge.
    /// Item-charging badges are out of scope: no delayed/charging item activation exists anywhere
    /// today (every consumable is Immediate) -- revisit if one is ever introduced. Called directly
    /// from DrawPrimaryOccupant, not from DrawEntityIcons, since the player must see their own
    /// charging badge too and DrawEntityIcons deliberately skips the player entirely.
    /// </summary>
    private void DrawChargingBadge(SpriteBatch spriteBatch, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (!_pendingDelayedActions.TryGetReadonly(entityId, out var pending) || !_actionCatalog.TryGet(pending.ActionId, out var action))
        {
            return;
        }

        SpriteComponent? sprite = null;
        if (action.SpriteName is { } spriteName && SpriteManifest.TryGetFirst(spriteName, out var resolvedSprite))
        {
            sprite = resolvedSprite;
        }

        var badgeSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y) * ChargingBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + (footprintSize.X - badgeSize.X) / 2f, footprintTopLeft.Y - badgeSize.Y);

        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, sprite, _badgeFont, action.Glyph, action.GlyphColor, badgePosition, badgeSize, Color.White, outline: true);
    }

    private void DrawEntityIcons(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (entityId == _world.PlayerEntityId)
        {
            return;
        }

        DrawHealthBar(spriteBatch, unitRectangle, entityId, footprintTopLeft, footprintSize);

        // A container is lootable while alive, unlike a creature (only lootable once dead, see
        // DrawCorpses' own call to the same badge helper) -- gated on ContainerComponent, not
        // just "carries items," since a live goblin/fairy/player also carries its own inventory
        // stacks and must not show a loot-bag badge for those.
        if (_containerPool?.Has(entityId) == true)
        {
            DrawLootBagBadgeIfCarryingItems(spriteBatch, entityId, footprintTopLeft, footprintSize);
        }
    }

    /// <summary>Thin bar at the top of the entity's own footprint, above its glyph, hidden at full health. Black backdrop doubles as the outline and the "missing health" portion; the fill rect insets 1px and its width (not the outline's) scales with the health fraction.</summary>
    private void DrawHealthBar(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (!HealthQueries.TryGetTotals(_healthPool, _bodyParts, entityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumHealth, maximumHealth);
        if (effectiveMaximumHealth <= 0 || currentHealth >= effectiveMaximumHealth)
        {
            return;
        }

        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = footprintTopLeft.Y;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var healthFraction = currentHealth / effectiveMaximumHealth;
        var innerWidth = (int)((outerRectangle.Width - 2) * healthFraction);
        if (innerWidth > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, innerWidth, HealthBarHeightPixels - 2), HealthBarPalette.FractionColor(healthFraction));
        }
    }

    /// <summary>Medium/large/huge glyph font by an entity's TransformComponent.Size.X -- shared by DrawMainGlyph and DrawPhasingGlyphs.</summary>
    private SpriteFontBase FontForSize(int sizeX) => sizeX switch
    {
        1 => _mediumFont,
        2 => _largeFont,
        _ => _hugeFont,
    };


    /// <summary>
    /// Blue up-arrow (top-right) if any layer above the current one is occupied; brown
    /// down-arrow (bottom-right) if any layer below is. A tile-level badge -- unlike
    /// DrawEntityIcons, this describes the tile's other layers, not the Blocking occupant
    /// drawn on it.
    /// </summary>
    /// <remarks>
    /// One Map.GetOccupiedLayerMask read plus two bit tests, rather than the per-layer
    /// IsPositionOccupied walk this used to do. That walk was Size.Z - 1 dictionary lookups on
    /// essentially every visible tile every frame, and at this game's real layer density it found
    /// nothing the overwhelming majority of the time -- measured at 17.4ms/sec, the single
    /// largest item in this window's whole draw path. The two masks are precomputed once per
    /// frame by the caller (see DrawGlyphs) since they depend only on the current layer, not on
    /// which tile is being drawn.
    /// </remarks>
    /// <param name="higherLayerMask">Bits for every layer above the current one -- see DrawGlyphs.</param>
    /// <param name="lowerLayerMask">Bits for every layer below the current one.</param>
    private void DrawLayerBadges(SpriteBatch spriteBatch, int higherLayerMask, int lowerLayerMask, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        var occupiedLayers = _world.Map.GetOccupiedLayerMask(mapNodeX, mapNodeY);
        if (occupiedLayers == 0)
        {
            return;
        }

        var hasHigherLayer = (occupiedLayers & higherLayerMask) != 0;
        var hasLowerLayer = (occupiedLayers & lowerLayerMask) != 0;

        if (hasHigherLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + _camera.CurrentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y);
            _labelRenderer.Draw(spriteBatch, _badgeFont, "^", drawPosition, UpLayerBadgeColor, outline: true);
        }

        if (hasLowerLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + _camera.CurrentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y + _camera.CurrentTileSize.Y - _badgeFont.LineHeight);
            _labelRenderer.Draw(spriteBatch, _badgeFont, "v", drawPosition, DownLayerBadgeColor, outline: true);
        }
    }

    /// <summary>Sets the camera to a specific zoom level directly, as opposed to CycleZoom's relative +/-1 step.</summary>
    /// <remarks>Resizes and resets the background cache afterward -- the visible tile count changes with zoom, so the cache's own buffer size and cached colors are both stale until rebuilt, the same cache-invalidation reasoning ChangeLayer/CenterCameraOn/CycleZoom each apply for their own trigger. No production caller yet (only MapWindowTests exercises this today) -- a candidate hook for a future zoom UI control (see the Minimap TODO item).</remarks>
    /// <param name="newZoomLevel">The zoom level to switch to.</param>
    public void UpdateZoomLevel(ZoomLevel newZoomLevel)
    {
        _camera.UpdateZoomLevel(newZoomLevel, ContentSize);
        _backgroundCache.Resize();
        InvalidateTerrainCaches();
    }

    private void CycleZoom(int direction)
    {
        _camera.CycleZoom(direction, ContentSize);
        _backgroundCache.Resize();
        InvalidateTerrainCaches();
    }

    /// <summary>Scrolls the camera by scrollChange tiles, keeping the background cache in sync.</summary>
    /// <remarks>MapCamera.UpdateScrollPosition clamps against the map edge and returns how much of the requested delta was actually applied -- _backgroundCache.ApplyScroll shifts the cache's existing colors by exactly that applied amount rather than rebuilding wholesale, so a clamped scroll (e.g. dragging past the map edge) doesn't shift the cache further than the camera itself actually moved.</remarks>
    /// <param name="scrollChange">The requested scroll delta, in tiles.</param>
    public void UpdateScrollPosition(Point scrollChange)
    {
        var appliedDelta = _camera.UpdateScrollPosition(scrollChange);
        ApplyCameraScrollToCaches(appliedDelta);
    }

    /// <summary>
    /// Shifts the per-visible-tile caches by a scroll delta the camera has already applied --
    /// shared by every camera move (drag, drag-end snap, and camera-follow's own re-centre), so
    /// they all get the same incremental treatment rather than one of them rebuilding wholesale.
    /// </summary>
    /// <remarks>
    /// The background cache shifts its known cells and re-resolves only what scrolled into view.
    /// The two render-target caches can't do that -- their content is a texture, not an array of
    /// resolved values -- so they simply rebuild, but only when the camera genuinely moved. A
    /// no-op delta (the common case while standing still, and every step taken along a map edge)
    /// leaves all three untouched.
    /// </remarks>
    /// <param name="appliedDelta">The scroll change the camera actually applied, in tiles.</param>
    private void ApplyCameraScrollToCaches(Point appliedDelta)
    {
        if (appliedDelta == Point.Zero)
        {
            return;
        }

        _backgroundCache.ApplyScroll(appliedDelta.X, appliedDelta.Y);
        _terrainCache?.Invalidate();
        _glowCache?.Invalidate();
    }

    /// <summary>Sets MapViewState.SelectedMapNodePosition to whatever map tile mousePosition resolves to, if any.</summary>
    /// <remarks>A miss (cursor off the map) is a no-op -- the previous selection, if any, stays selected rather than being cleared by clicking empty space. This is the ordinary inspector click-select path; see OnContentClickAction for why an armed ability/item's click-to-confirm takes over first when something is armed.</remarks>
    /// <param name="mousePosition">The raw mouse position (e.g. from Mouse.GetState()), not pre-translated to this window's content area -- resolved against _contentState.AbsolutePosition internally, the same as UpdateHoveredTile/TryConfirmActivation.</param>
    public void SelectMapNodes(Point mousePosition)
    {
        if (_camera.TryGetHoveredMapPosition(mousePosition, _contentState.AbsolutePosition, out var mapPosition))
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
            _actionTargeting.TryConfirmActivation(mousePosition, _contentState.AbsolutePosition);
            return;
        }

        SelectMapNodes(mousePosition);
    }

    /// <summary>A player can only loot a corpse they're standing on or next to -- 8-directional (Chebyshev) distance of at most 1 from the corpse's own origin tile, the same adjacency shape TargetShape.Adjacent's ring uses elsewhere, just inclusive of the caster's own tile too (unlike melee's ring, which excludes it -- standing on a corpse to loot it is expected, unlike punching yourself).</summary>
    private bool IsAdjacentToPlayer(int entityId) =>
        _transformPool.TryGetReadonly(entityId, out var corpseTransform) &&
        _transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform) &&
        GridDistance.ChebyshevDistance(corpseTransform.Position, playerTransform.Position) <= 1;

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
    /// occupant (world.GetOccupantEntityIdsAt, Blocking or not) plus the terrain, each its own
    /// group: a read-only name header (see ContextMenuOption.Header -- also the visual separator
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
        if (!_camera.TryGetHoveredMapPosition(mousePosition, _contentState.AbsolutePosition, out var mapPosition))
        {
            return;
        }

        var tilePosition = new Vector3Int(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer);

        List<ContextMenuOption> options = [];

        foreach (var entityId in _world.GetOccupantEntityIdsAt(tilePosition))
        {
            AddEntityGroup(options, entityId);
        }

        if (Map.TerrainLayerFor(_mapViewState.CurrentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = _world.Map.GetTerrainEntityId(mapPosition.X, mapPosition.Y, terrainLayer);
            if (terrainEntityId != -1)
            {
                AddEntityGroup(options, terrainEntityId);
            }
        }

        if (options.Count > 0)
        {
            _contextMenuController.Open(new Vector2(mousePosition.X, mousePosition.Y), options);
        }
    }

    /// <summary>Appends one contributor's own group to the tile's stacked menu -- a read-only name header, then whatever options it offers. Works identically for a creature occupant or the terrain entity itself, since both are just an entityId with a DisplayTextComponent -- terrain simply never has a DeadComponent/ContainerComponent/ShopComponent, so it never picks up "Loot"/"Shop".</summary>
    private void AddEntityGroup(List<ContextMenuOption> options, int entityId)
    {
        options.Add(ContextMenuOption.Header(ResolveName(entityId)));

        var isShop = _shopPool?.Has(entityId) == true;

        // A shop that's died goes through the same EntityDiedEvent -> DeadComponent pipeline as any
        // other SimpleHealthComponent entity (DeathSystem doesn't special-case containers/shops out
        // of it -- see its own doc comment), even though ContainerDestructionSystem's own handling
        // for the same event leaves its ShopComponent/ContainerComponent in place (renaming it
        // "Destroyed" and clearing its stock, not removing the components). So isShop alone can't
        // tell a live shop apart from a destroyed one -- isDestroyed does.
        var isDestroyed = _deadPool?.Has(entityId) == true;

        // A shop is still a ContainerComponent (see Shop's own doc comment), but gets its own
        // "Shop" verb instead of the generic corpse/chest "Loot" one while it's alive -- excluded
        // here so a live shop never offers both. A destroyed shop is the opposite: "Shop" no longer
        // makes sense (nothing left to trade), so it falls through to the plain "Loot" a dead
        // creature or a destroyed chest already gets.
        if ((isDestroyed || (_containerPool?.Has(entityId) == true && !isShop)) && OnCorpseClicked is { } onCorpseClicked)
        {
            options.Add(new ContextMenuOption("Loot", null, IsAdjacentToPlayer(entityId), () => onCorpseClicked.Invoke(entityId)));
        }

        if (isShop && !isDestroyed && OnShopClicked is { } onShopClicked)
        {
            options.Add(new ContextMenuOption("Shop", null, IsAdjacentToPlayer(entityId), () => onShopClicked.Invoke(entityId)));
        }

        options.Add(new ContextMenuOption("Inspect", null, !ActionLockGate.IsBlocked(_actionLockPool, _world.PlayerEntityId), () => InspectEntity(entityId)));
    }

    private string ResolveName(int entityId) => _displayTextPool.TryGetReadonly(entityId, out var displayText) ? displayText.Name : "Unknown";

    /// <summary>Details/Admin inspection's actual activation -- sets Detail or Admin mode (GlobalState.IsAdminModeOn) on the shared entityId (see MapViewState.InspectedEntityId), starts the global cooldown (the same shared ActionLockComponent lock movement/melee/consumables already use), and un-minimizes InspectionWindow. Only ever reached via the "Inspect" ContextMenuOption above, which already gates on the cooldown being clear -- no redundant re-check here, matching how "Loot" above trusts its own Enabled gate instead of re-checking adjacency.</summary>
    private void InspectEntity(int entityId)
    {
        _mapViewState.InspectionMode = GlobalState.IsAdminModeOn ? InspectionMode.Admin : InspectionMode.Detail;
        _mapViewState.InspectedEntityId = entityId;
        ActionLockGate.Lock(_actionLockPool, _world.PlayerEntityId);
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
            _camera.ResumeFollowingPlayer();
            if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform))
            {
                SnapCameraToPlayer(playerTransform.Position);
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
            _playerMovement.HandleInput(keyboardState, _claimedKeysThisFrame);
            _actionTargeting.HandleHotbarHotkeys(keyboardState, previousKeyboardState);
        }
    }

    /// <summary>
    /// Re-centres the camera and brings the per-visible-tile caches with it -- incrementally when
    /// the camera actually moved, and not at all when it didn't.
    /// </summary>
    /// <remarks>
    /// Camera-follow calls this on every player step, so this is the hottest invalidation path in
    /// the window. It used to unconditionally call MapBackgroundCache.Reset -- a full re-resolve of
    /// every visible cell, each costing an IsOnMap check plus up to two BackgroundComponent pool
    /// reads -- for what is almost always a one-tile shift, while the very same class already had
    /// ApplyScroll for exactly this case. Routing through UpdateScrollPosition instead means a
    /// player step shifts the known cells and re-resolves only the newly-exposed row or column, and
    /// a step that doesn't move the camera at all (against a map edge, or the follow already
    /// centred) costs nothing.
    /// </remarks>
    private void CenterCameraOn(Vector3Int position)
    {
        var appliedDelta = _camera.CenterCameraOn(position);
        ApplyCameraScrollToCaches(appliedDelta);
    }

    /// <summary>Snapshots the scroll position the moment a right-mouse-drag starts, so OnRightDragAction always has a fixed anchor to measure the drag against.</summary>
    protected override void OnRightDragStartAction() => _camera.BeginDrag();

    protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart)
    {
        if (totalPixelDeltaSinceStart == Vector2.Zero)
        {
            return;
        }

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