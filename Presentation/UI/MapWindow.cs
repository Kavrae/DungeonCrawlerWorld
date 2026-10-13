using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using FontStashSharp;
using Game.Modules.Actions.Definitions.DirectActions;
using SpriteFormat;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.FloatingText;

namespace Presentation.UI;

/// <summary>Displays a scrollable/zoomable viewport onto a single MapLayer of the game map at a time.</summary>
/// <remarks>
/// The map-rendering composition root: owns the draw order (flat fill, terrain, glyphs/sprites, glow,
/// targeting/selection highlights) and routes its own input hooks (hotkeys, clicks, right-drag)
/// to whichever collaborator actually owns that concern -- MapCamera (pan/zoom),
/// AuraGlowBlend (aura glow, from AuraPowerView), ActionTargetingController (arm/target/
/// confirm), PlayerMovementController (WASD movement). MapWindow itself should stay thin glue
/// over those, not accumulate gameplay logic of its own.
///
/// Reads the world only through IMapViewQuery (PLAN-presentation-data-layer.md, Stage 2) -- never a
/// component pool. MapWindowArchitectureTests enforces that.
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

    /// <summary>A Phasing occupant draws at half opacity over the Blocking one sharing its tiles, so it reads as translucently in front rather than hidden.</summary>
    private const float PhasingAlphaMultiplier = 0.5f;

    /// <summary>Fraction of a tile's own size the charging-action badge renders at, matching LootBagBadgeSizeFraction's own scale.</summary>
    private const float ChargingBadgeSizeFraction = 0.4f;

    /// <summary>Fraction of TargetSelectionMaskAlpha used for the always-visible full-tile backdrop drawn under the growing charge fill -- keeps a multi-tile Delayed action's whole target shape legible from the first frame of the windup, not just once each tile's own fill has grown enough to be seen.</summary>
    private const float ChargeBackdropAlphaFraction = 0.3f;

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

    /// <summary>Null until the first Update that finds a GraphicsDevice on ElementPoolService, and permanently null for a headless MapWindow that never gets one -- DrawContent's own fallback paths cover both cases. See Update for why these can't be built in Initialize.</summary>
    private MapTileLayerCache? _terrainCache;

    /// <summary>The aura glow overlay's own cached rendering -- a separate texture from _terrainCache rather than the same one because it is blitted on the other side of the occupants (see DrawGlowOverlay).</summary>
    private MapTileLayerCache? _glowCache;

    /// <summary>The AuraPowerView.Version _glowCache was last rendered against -- the glow texture depends on the aura field's contents as well as on the camera, so a source appearing, moving or expiring has to invalidate it even though nothing about the camera changed.</summary>
    private int _renderedGlowVersion = -1;

    private readonly AuraPowerView _auraPower;
    private readonly AuraGlowBlend _auraGlowBlend;

    /// <summary>This frame's "an earlier hotkey handler already used this key" set -- cleared and repopulated every OnHotkeysAction call, before PlayerMovementController.HandleInput reads it. See that class's own doc comment for why this exists (today: Dodge's directional confirm claiming WASD ahead of plain movement).</summary>
    private readonly HashSet<Keys> _claimedKeysThisFrame = [];

    private readonly LabelRenderer _labelRenderer;
    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

    private const float TargetSelectionMaskAlpha = 0.5f;

    /// <summary>The summed aura power at a tile that glows at full brightness; anything more glows no brighter.</summary>
    private const int FullGlowPower = 8;

    /// <summary>Halves the glow so a full-strength one still lets whatever's standing on that tile -- terrain, an occupant sprite/glyph -- read through, rather than washing it out at the source tile itself.</summary>
    private const float GlowOpacityMultiplier = 0.5f;

    private static readonly Color MapFillColor = new(40, 40, 40);

    /// <summary>What a tile off the loaded map is drawn as, so the edge of the world reads as an edge rather than as floor with nothing on it.</summary>
    private static readonly Color OffMapColor = Color.Black;

    private SpriteFontBase _mediumFont = null!;
    private SpriteFontBase _largeFont = null!;
    private SpriteFontBase _hugeFont = null!;
    private SpriteFontBase _tinyFont = null!;
    private SpriteFontBase _badgeFont = null!;

    /// <summary>
    /// LootBag-Red resolved once, not per badge per frame -- a sprite lookup by name is a
    /// string-keyed dictionary lookup, and repeating it for every corpse badge of every frame is
    /// needless work on the draw path. The first cell rather than a random one because a badge has
    /// to look the same on every corpse.
    /// </summary>
    private SpriteView? _lootBagSprite;

    /// <summary>What the occupant pass draws and in what order, re-planned every frame into the same buffers.</summary>
    private readonly MapDrawOrder _drawOrder = new();

    /// <summary>Which walls' faces and tops are cached and which the row walk draws live; re-planned whenever the cached terrain image is rendered (DrawStructures), so its overhangs always match the image.</summary>
    private readonly MapWallPlan _wallPlan = new();

    /// <summary>The terrain and structures around the grid, read once per rebuild of the cached terrain image (DrawTerrainDirectly) and resolved for drawing.</summary>
    private readonly MapTerrainCells _terrainCells;

    /// <summary>The entities whose drawn sprites overhangs fade over this frame, and those sprites' rectangles (OverhangFade). Fields so they aren't reallocated per frame.</summary>
    private readonly List<int> _watchedEntityIds = [];

    private readonly List<Rectangle> _watchedRectangles = [];

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
        AuraPowerView auraPower,
        EventBus eventBus,
        LabelRenderer labelRenderer,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer,
        MapCamera camera,
        ActionTargetingController actionTargeting,
        PlayerMovementController playerMovement,
        ContextMenuController contextMenuController,
        FloatingTextController floatingTextController,
        FloatingTextRenderer floatingTextRenderer,
        AdminContextMenuOptions adminContextMenuOptions) : base(fontService, elementPoolService, labelRenderer)
    {
        _mapView = mapView;
        _playerActionGate = playerActionGate;
        _mapViewState = mapViewState;
        _labelRenderer = labelRenderer;
        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;

        _camera = camera;
        _actionTargeting = actionTargeting;
        _playerMovement = playerMovement;
        _contextMenuController = contextMenuController;
        _adminContextMenuOptions = adminContextMenuOptions;
        _auraPower = auraPower;
        _auraGlowBlend = new AuraGlowBlend(auraPower);
        _terrainCells = new MapTerrainCells(mapView, spriteSheetService.Sprites);
        _floatingTextController = floatingTextController;
        _floatingTextRenderer = floatingTextRenderer;

        // Terrain and structures are what MapWindow draws that a rare, explicit event can invalidate
        // rather than per-frame change -- see MapTileLayerCache. World.SetTerrain and
        // World.SetStructure publish these for every runtime change.
        eventBus.Subscribe<TerrainChangedEvent>(_ => InvalidateTerrainCaches());
        eventBus.Subscribe<StructureChangedEvent>(_ => InvalidateTerrainCaches());
        eventBus.Subscribe<TerrainLoadedEvent>(loaded => InvalidateTerrainCachesIfVisible(loaded.Area));
        eventBus.Subscribe<TerrainUnloadingEvent>(unloading => InvalidateTerrainCachesIfVisible(unloading.Area));

        _tileDepth = mapView.Bounds.Depth;
    }

    /// <summary>One-time setup once this window's own content size is known -- font loading, camera sizing, and the initial camera position.</summary>
    /// <remarks>Snaps the camera to the player's spawn position if it already exists at this point, otherwise invalidates the terrain caches instead. In real gameplay the player already exists by the time this ever runs -- WorldSessionBootstrapper.Build spawns it before ShellBootstrapper.Build ever constructs a MapWindow -- but this still has to tolerate the player not existing, for a MapWindow built directly (e.g. tests) without going through that same sequence.</remarks>
    public override void Initialize()
    {
        base.Initialize();

        _mediumFont = FontService.GetFont(FontChrome.MapMediumFontSize);
        _largeFont = FontService.GetFont(FontChrome.MapLargeFontSize);
        _hugeFont = FontService.GetFont(FontChrome.MapHugeFontSize);
        _tinyFont = FontService.GetFont(FontChrome.MapTinyFontSize); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = FontService.GetFont(FontChrome.MapBadgeFontSize); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        _camera.Initialize(ContentSize);

        _lootBagSprite = _spriteSheetService.Sprites.TryGetFirst(LootBagSpriteName, out var lootBagSprite) ? lootBagSprite : null;

        SetCurrentMapLayer(_mapViewState.CurrentMapLayer);

        if (TryGetPlayerPosition(out var playerPosition))
        {
            SnapCameraToPlayer(playerPosition);
            _camera.LastKnownPlayerPosition = playerPosition;
        }
        else
        {
            InvalidateTerrainCaches();
        }
    }

    /// <summary>The map bounds the camera's scroll limits were last computed from -- the sliding window changes them as neighborhoods load and unload.</summary>
    private MapBounds _scrollLimitBounds;

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
        _floatingTextController.Update();

        if (_mapView.Bounds != _scrollLimitBounds)
        {
            _scrollLimitBounds = _mapView.Bounds;
            _camera.RefreshScrollLimits();
            InvalidateTerrainCaches();
        }

        if (TryGetPlayerPosition(out var playerPosition) && playerPosition != _camera.LastKnownPlayerPosition)
        {
            _camera.LastKnownPlayerPosition = playerPosition;

            if (_camera.FollowsPlayer)
            {
                CenterCameraOn(playerPosition);
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

        if (_renderedGlowVersion != _auraPower.Version)
        {
            _renderedGlowVersion = _auraPower.Version;
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
            DrawTerrainIntoCache);

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
    /// invalidating them as a pair is what stops the terrain image and the glow from ever
    /// disagreeing about which cells they describe.
    /// </summary>
    private void InvalidateTerrainCaches()
    {
        _terrainCache?.Invalidate();
        _glowCache?.Invalidate();
    }

    /// <summary>InvalidateTerrainCaches, only when area overlaps the tiles the cache draws from: a neighborhood streams in a row at a time, mostly out of view.</summary>
    /// <remarks>Widened by the walls' overscan (see MapWallPlan) plus one tile, since a wall just outside the grid can reach into it and a tile next to a wall decides whether it shows its face.</remarks>
    private void InvalidateTerrainCachesIfVisible(MapBounds area)
    {
        var scroll = _camera.CurrentScrollPosition;
        var beside = SpriteReach.MaximumColumnsBeside + 1;
        var below = SpriteReach.MaximumRowsAbove + 1;
        if (area.MinX < scroll.X + _camera.TileColumns + beside && area.MaxX > scroll.X - beside && area.MinY < scroll.Y + _camera.TileRows + below && area.MaxY > scroll.Y)
        {
            InvalidateTerrainCaches();
        }
    }

    /// <summary>
    /// Delegates to ActionTargetingController.UpdateHoveredTile -- kept as a method on MapWindow
    /// (internal, not private) since MapWindowTests exercises it directly the same way it does
    /// Window.HandleHotkeys, simulating a mouse position without a real OS cursor.
    /// </summary>
    internal void UpdateHoveredTile(Point mousePosition) => _actionTargeting.UpdateHoveredTile(mousePosition, _contentState.AbsolutePosition);

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
        CenterCameraOn(position);
    }

    /// <summary>The single place [0, _tileDepth - 1] clamping happens for MapViewState.CurrentMapLayer -- shared by ChangeLayer, SnapToPlayer, and Initialize's own re-clamp against whatever depth this particular Map turns out to have.</summary>
    private void SetCurrentMapLayer(int layer)
    {
        _mapViewState.CurrentMapLayer = MathUtility.ClampInt(layer, 0, _tileDepth - 1);
    }

    /// <summary>Draws one frame of the map viewport: the flat fill, terrain, glyphs/sprites, glow overlay, then targeting/selection highlights, in that order.</summary>
    /// <remarks>
    /// Draw order is significant, not incidental -- each pass lands on top of the previous one with
    /// no depth buffer (SpriteSortMode.Deferred submits in call order), so highlights/glow have to
    /// come after the glyphs/sprites they're meant to sit on top of, and the flat fill has to come
    /// first so everything else has something to draw over. A terrain's sprite is its background:
    /// terrain with none shows the flat fill under its glyph.
    ///
    /// Terrain (floors, walls, and black for off-map tiles) comes from MapTileLayerCache as
    /// a single blit rather than being re-submitted tile-by-tile -- see that class for why terrain
    /// is the one thing here that can be cached as an image. DrawTerrainDirectly is
    /// the fallback for the frame or two before the cache's first render lands (and for a headless
    /// MapWindow that never got a GraphicsDevice), producing byte-identical output at the old cost.
    /// </remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, _camera.TileColumns * _camera.CurrentTileSize.X, _camera.TileRows * _camera.CurrentTileSize.Y), MapFillColor);

        if (_terrainCache?.Draw(spriteBatch, -_camera.RenderPixelOffset) != true)
        {
            DrawTerrainDirectly(spriteBatch, _camera.RenderPixelOffset);
        }

        DrawOccupants(spriteBatch, unitRectangle);

        if (_glowCache?.Draw(spriteBatch, -_camera.RenderPixelOffset) != true)
        {
            DrawGlowOverlay(spriteBatch, unitRectangle, _camera.RenderPixelOffset);
        }

        DrawTargetingHighlights(spriteBatch, unitRectangle);

        if (_mapViewState.InspectionMode == InspectionMode.Detail)
        {
            DrawFollowedEntityHighlight(spriteBatch, unitRectangle);
        }
        else
        {
            DrawSelectedTileHighlight(spriteBatch, unitRectangle);
        }

        _floatingTextRenderer.Draw(spriteBatch, _mapViewState.CurrentMapLayer);
    }

    /// <summary>
    /// The auras' glow (see AuraGlowBlend), drawn as a translucent overlay on top of terrain and
    /// occupant sprites, so it shows the same over a full-tile sprite as over a glyph on the flat fill.
    /// </summary>
    /// <summary>Renders the glow overlay at whole-tile positions with no sub-tile offset, for MapTileLayerCache to capture -- the caller applies the offset once when blitting.</summary>
    private void DrawGlowOverlayIntoCache(SpriteBatch spriteBatch) =>
        DrawGlowOverlay(spriteBatch, ElementPoolService.UnitRectangle, Vector2.Zero);

    private void DrawGlowOverlay(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 pixelOffset)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;
        _auraGlowBlend.BeginPass();

        for (var columnIndex = 0; columnIndex < _camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _camera.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _camera.CurrentScrollPosition.X;
                var mapNodeY = rowIndex + _camera.CurrentScrollPosition.Y;

                if (!_mapView.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)) || !_auraGlowBlend.TryGetGlow(mapNodeX, mapNodeY, currentMapLayer, out var glowColor, out var totalPower))
                {
                    continue;
                }

                var tileOrigin = new Vector2(columnIndex * _camera.CurrentTileSize.X, rowIndex * _camera.CurrentTileSize.Y) - pixelOffset;
                var destination = new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
                var glowFactor = Math.Min(totalPower, FullGlowPower) / (float)FullGlowPower;
                spriteBatch.Draw(unitRectangle, destination, glowColor * glowFactor * GlowOpacityMultiplier);
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
    /// ActionTargetingController.PendingWindupTargetTiles (the already-resolved,
    /// locked-in footprint) in the same dark green used for a confirmed hover target, for as long
    /// as that pending action exists. Separately (and unconditionally, every frame, regardless of
    /// the player's own armed/pending state above), every OTHER entity's own in-flight Delayed
    /// windup is telegraphed too, in red or yellow depending on whether it's Dodgeable -- see
    /// CombatTargetPalette and ActionTargetingController.AllPendingWindupTargets. This is
    /// the "delayed actions keep the target shape drawn on the map... until they activate" half of
    /// Combat Overhaul: Dodge (TODO.md).
    /// </summary>
    private void DrawTargetingHighlights(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
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
        else if (_actionTargeting.PendingWindupTargetTiles is { } pendingTargetTiles)
        {
            var fillFraction = _mapView.GetChargeFraction(_mapView.PlayerEntityId);
            foreach (var tile in pendingTargetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor, fillFraction);
            }
        }

        // Every OTHER entity's own Delayed-action telegraph -- red (undodgeable) or yellow
        // (Dodgeable), per Combat Overhaul: Dodge. The player's own is already drawn above (dark
        // green, "player target"), so it's skipped here to avoid drawing it twice.
        // AllPendingWindupTargets itself already scopes this to Local processing tier (see
        // that method's own doc comment) -- MapWindow doesn't need its own second tier check here.
        foreach (var (entityId, targetTiles, isDodgeable) in _actionTargeting.AllPendingWindupTargets())
        {
            if (entityId == _mapView.PlayerEntityId)
            {
                continue;
            }

            var borderColor = isDodgeable ? CombatTargetPalette.EnemyDodgeableColor : CombatTargetPalette.EnemyUndodgeableColor;
            var fillFraction = _mapView.GetChargeFraction(entityId);
            foreach (var tile in targetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor, fillFraction);
            }
        }

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
        if (_mapViewState.ArmedActionId != DodgeAction.Id || !TryGetPlayerPosition(out var playerPosition))
        {
            return;
        }

        foreach (var (offset, label) in DodgeDirectionalHints)
        {
            var tile = playerPosition + offset;
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
        if (entityId == -1 || !_mapView.TryGetOccupant(entityId, out var followed))
        {
            return;
        }

        for (var offsetX = 0; offsetX < followed.Size.X; offsetX++)
        {
            for (var offsetY = 0; offsetY < followed.Size.Y; offsetY++)
            {
                DrawSelectedTileGlow(spriteBatch, unitRectangle, followed.Position.X + offsetX, followed.Position.Y + offsetY);
            }
        }
    }

    /// <summary>
    /// A uniform translucent borderColor wash (TargetSelectionMaskAlpha alpha) over the whole
    /// tile -- shared by the inspector's single-tile Gold selection and DrawTargetingHighlights'
    /// per-tile ability-targeting colors. Drawn after DrawGlyphs/DrawGlowOverlay (not before,
    /// like terrain) so it lands on top of whatever's actually on the tile --
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
    /// A charging Delayed action's own per-tile telegraph -- an always-visible, dim full-tile
    /// backdrop (ChargeBackdropAlphaFraction of DrawMaskedTileHighlight's own alpha, so a
    /// multi-tile target shape still reads as one coherent zone from the first frame of the
    /// windup) plus a brighter bottom-up fill on top that grows with fillFraction, reaching
    /// DrawMaskedTileHighlight's own full alpha -- and its own full-tile coverage -- exactly as
    /// the action activates.
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
    /// the inspector to the same layer this window is actually showing. The terrain image is the
    /// current layer's, so it must be rebuilt on every change, the same as a zoom-level change.
    /// Called from OnHotkeysAction (Page Up/Down).
    /// </remarks>
    /// <param name="delta">Layers to move by -- positive moves up, negative moves down (clamped to the map's own depth by SetCurrentMapLayer).</param>
    public void ChangeLayer(int delta)
    {
        SetCurrentMapLayer(_mapViewState.CurrentMapLayer + delta);
        InvalidateTerrainCaches();
    }

    /// <summary>
    /// Renders the terrain at whole-tile positions with no sub-tile offset,
    /// for MapTileLayerCache to capture into its texture -- the caller applies the offset once when
    /// blitting that texture instead of every tile applying it individually.
    /// </summary>
    /// <remarks>
    /// Terrain is drawn as its own full pass, ahead of every occupant, and that ordering is
    /// load-bearing rather than incidental: a multi-tile entity's sprite is drawn once from its
    /// origin tile covering its whole footprint, so a neighbouring tile's terrain draw -- a later
    /// call, and SpriteSortMode.Deferred submits in call order with no depth buffer -- would
    /// otherwise land on top of part of that footprint. CurrentFrame that terrain lives in its own texture
    /// blitted before any occupant, that separation is structural rather than something the loop
    /// order has to keep getting right.
    /// </remarks>
    private void DrawTerrainIntoCache(SpriteBatch spriteBatch)
    {
        using (EngineHooks.FrameCost(FrameCostCategory.Update, "Map", "Terrain cache rebuild"))
        {
            DrawTerrainDirectly(spriteBatch, Vector2.Zero);
        }
    }

    /// <summary>The uncached path: the same floors, walls and off-map black MapTileLayerCache captures, drawn straight to the screen at the given sub-tile offset. Used to render into the cache (offset zero) and as DrawContent's fallback before the cache's first render lands. Either way it rebuilds the overhang list the row walk draws, so the list always matches the image.</summary>
    private void DrawTerrainDirectly(SpriteBatch spriteBatch, Vector2 pixelOffset)
    {
        var tileSize = _camera.CurrentTileSize;
        _terrainCells.Read(_camera.CurrentScrollPosition, _camera.TileColumns, _camera.TileRows, _mapViewState.CurrentMapLayer);

        for (var columnIndex = 0; columnIndex < _camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _camera.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _camera.CurrentScrollPosition.X;
                var mapNodeY = rowIndex + _camera.CurrentScrollPosition.Y;
                var tileOrigin = new Vector2(columnIndex * tileSize.X, rowIndex * tileSize.Y) - pixelOffset;

                if (!_terrainCells.IsOnMap(mapNodeX, mapNodeY))
                {
                    spriteBatch.Draw(ElementPoolService.UnitRectangle, new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, tileSize.X, tileSize.Y), OffMapColor);
                    continue;
                }

                DrawFloor(spriteBatch, mapNodeX, mapNodeY, tileOrigin);
            }
        }

        DrawStructures(spriteBatch, pixelOffset);
    }

    /// <summary>Every wall's face and top that MapWallPlan caches, faces then tops; the rest it leaves on its Overhangs for the row walk.</summary>
    private void DrawStructures(SpriteBatch spriteBatch, Vector2 pixelOffset)
    {
        _wallPlan.Plan(_terrainCells, _camera.CurrentScrollPosition, _camera.TileColumns, _camera.TileRows);

        foreach (var piece in _wallPlan.CachedPieces)
        {
            DrawStructurePiece(spriteBatch, piece, pixelOffset);
        }
    }

    /// <summary>A wall's face or top on the tile it sits on, sized and placed by its sprite's placement.</summary>
    private void DrawStructurePiece(SpriteBatch spriteBatch, MapOverhang piece, Vector2 pixelOffset)
    {
        var scroll = _camera.CurrentScrollPosition;
        var tileSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
        var tileOrigin = new Vector2((piece.AnchorX - scroll.X) * tileSize.X, (piece.AnchorY - scroll.Y) * tileSize.Y) - pixelOffset;
        DrawVisual(spriteBatch, piece.Visual, _mediumFont, tileOrigin, tileSize, new Vector2Byte(1, 1), out _);
    }

    /// <summary>Every occupant of the visible grid, drawn on top of MapTileLayerCache's already-blitted terrain -- corpses, the Tiny sub-grid, Blocking occupants, Phasing overlays, then every tile's up/down layer badges -- in the order MapDrawOrder plans.</summary>
    /// <remarks>
    /// Rows top to bottom, each row left to right, anything that stands up drawn from the lowest row of its footprint (see
    /// MapDrawOrder): a later row overlaps an earlier one, which is what lets a sprite reach above its own tile. The order is
    /// chosen for what overlaps what, not for speed -- Map's per-cell arrays are X-fastest, and walking them row-major or
    /// column-major was measured to make no difference against the same warm cache. SpriteSortMode.Deferred draws in call
    /// order with no depth buffer, so the plan's order is the draw order.
    /// </remarks>
    private void DrawOccupants(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var scroll = _camera.CurrentScrollPosition;
        _drawOrder.Plan(_mapView, scroll, _camera.TileColumns, _camera.TileRows, _mapViewState.CurrentMapLayer, _tileDepth, _wallPlan.Overhangs);
        CollectWatchedRectangles(scroll);

        var tinyTile = new Point(int.MinValue, int.MinValue);
        var tinyDrawnCount = 0;

        foreach (var item in _drawOrder.Items)
        {
            var footprintTopLeft = TileOrigin(item.MapX - scroll.X, item.MapY - scroll.Y);
            var footprintSize = new Vector2(item.Size.X * _camera.CurrentTileSize.X, item.Size.Y * _camera.CurrentTileSize.Y);

            switch (item.Kind)
            {
                case MapDrawItemKind.Corpse:
                    TryDrawEntityVisual(spriteBatch, item.EntityId, FontForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size);
                    DrawLootBagBadge(spriteBatch, _mapView.GetStatus(item.EntityId).LootBag, footprintTopLeft, footprintSize);
                    break;

                case MapDrawItemKind.Tiny:
                    if (tinyTile.X != item.MapX || tinyTile.Y != item.MapY)
                    {
                        tinyTile = new Point(item.MapX, item.MapY);
                        tinyDrawnCount = 0;
                    }

                    if (tinyDrawnCount < MaxTinyEntitiesDrawn && DrawTinyOccupant(spriteBatch, item.EntityId, footprintTopLeft, tinyDrawnCount))
                    {
                        tinyDrawnCount++;
                    }

                    break;

                case MapDrawItemKind.Blocking:
                    DrawPrimaryOccupant(spriteBatch, unitRectangle, item.EntityId, item.Size, footprintTopLeft, footprintSize);
                    break;

                case MapDrawItemKind.Phasing:
                    TryDrawEntityVisual(spriteBatch, item.EntityId, FontForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size, PhasingAlphaMultiplier);
                    break;

                case MapDrawItemKind.LayerBadge:
                    DrawLayerBadges(spriteBatch, item.HasHigherLayer, item.HasLowerLayer, footprintTopLeft);
                    break;

                case MapDrawItemKind.Overhang:
                    DrawOverhang(spriteBatch, _wallPlan.Overhangs[item.EntityId], footprintTopLeft, footprintSize);
                    break;
            }
        }
    }

    /// <summary>Where the player's sprite -- and the inspected entity's, in Detail or Admin inspection -- is drawn this frame, for OverhangFade.</summary>
    private void CollectWatchedRectangles(Point scroll)
    {
        OverhangFade.CollectWatchedEntities(_watchedEntityIds, _mapView.PlayerEntityId, _mapViewState.InspectionMode, _mapViewState.InspectedEntityId);
        _watchedRectangles.Clear();

        foreach (var entityId in _watchedEntityIds)
        {
            if (!_mapView.TryGetOccupant(entityId, out var occupant) || !_mapView.TryGetVisual(entityId, out var visual))
            {
                continue;
            }

            var footprintTopLeft = TileOrigin(occupant.Position.X - scroll.X, occupant.Position.Y - scroll.Y);
            var footprintSize = new Vector2(occupant.Size.X * _camera.CurrentTileSize.X, occupant.Size.Y * _camera.CurrentTileSize.Y);
            _watchedRectangles.Add(OverhangFade.WatchedRectangle(footprintTopLeft, footprintSize, occupant.Size, _spriteSheetService.Sprites.Resolve(visual).Sprite));
        }
    }

    /// <summary>A wall's live face or top, faded where it is drawn over a watched entity (OverhangFade).</summary>
    private void DrawOverhang(SpriteBatch spriteBatch, MapOverhang overhang, Vector2 tileOrigin, Vector2 tileSize)
    {
        var overhangRectangle = OverhangFade.WatchedRectangle(tileOrigin, tileSize, new Vector2Byte(1, 1), overhang.Visual.Sprite);
        var alpha = OverhangFade.AlphaFor(overhangRectangle, _watchedRectangles);
        DrawVisual(spriteBatch, overhang.Visual, _mediumFont, tileOrigin, tileSize, new Vector2Byte(1, 1), out _, alpha);
    }

    /// <summary>Draws entityId's sprite if it has one, else falls back to its glyph -- see DrawVisual. Returns whether anything was actually drawn.</summary>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, float alphaMultiplier = 1f) =>
        TryDrawEntityVisual(spriteBatch, entityId, font, footprintTopLeft, footprintSize, footprintTiles, out _, alphaMultiplier);

    /// <inheritdoc cref="TryDrawEntityVisual(SpriteBatch, int, SpriteFontBase, Vector2, Vector2, Vector2Byte, float)"/>
    /// <param name="drawnTop">Where the top of what was drawn landed, for cues that sit above it; the footprint's top for a glyph or a miss.</param>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        return _mapView.TryGetVisual(entityId, out var visual) && DrawVisual(spriteBatch, _spriteSheetService.Sprites.Resolve(visual), font, footprintTopLeft, footprintSize, footprintTiles, out drawnTop, alphaMultiplier);
    }

    /// <summary>The one place that turns a resolved visual into a draw, shared by every per-tile visual draw below. A sprite with a placement is sized and positioned by it (SpritePlacementMath) -- it may reach above and beside its footprint -- and one without fills the footprint, as a glyph does. A corpse draws with a flat Color.Gray tint instead of its normal color -- a color-multiply override, not a true desaturation shader (no shader/Effect infrastructure exists here). Delegates the actual draw to SpriteOrGlyphRenderer, shared with Folder/inventory item cells.</summary>
    /// <param name="footprintTiles">The footprint in tiles, which a placed sprite scales with; null draws a sprite stretched to the footprint whatever its placement (terrain, until walls draw their faces and tops by placement).</param>
    private bool DrawVisual(SpriteBatch spriteBatch, MapVisual visual, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte? footprintTiles, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        if (visual.Sprite is not { } sprite)
        {
            return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, (SpriteView?)null, font, visual.Glyph, visual.IsDead ? Color.Gray : visual.GlyphColor, footprintTopLeft, footprintSize, Color.White, alphaMultiplier, outline: true);
        }

        var (drawnTopLeft, drawnSize) = footprintTiles is { } tiles
            ? SpritePlacementMath.DrawnRectangle(footprintTopLeft, footprintSize, tiles, new Point(sprite.SourceRectangle.Width, sprite.SourceRectangle.Height), sprite.Placement)
            : (footprintTopLeft, footprintSize);
        drawnTop = drawnTopLeft.Y;
        return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, sprite, font, string.Empty, Color.White, drawnTopLeft, drawnSize, visual.IsDead ? Color.Gray : Color.White, alphaMultiplier, outline: true);
    }

    /// <summary>One Tiny occupant in the tile's 3x3 sub-grid, at the cell for how many of the tile's Tiny occupants were drawn before it. Returns whether anything was drawn, so an occupant with no visual doesn't take a cell.</summary>
    private bool DrawTinyOccupant(SpriteBatch spriteBatch, int entityId, Vector2 tileOrigin, int drawnBefore)
    {
        var subCellSize = new Point(_camera.CurrentTileSize.X / TinyGridDimension, _camera.CurrentTileSize.Y / TinyGridDimension);
        var subColumn = drawnBefore % TinyGridDimension;
        var subRow = drawnBefore / TinyGridDimension;
        var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

        return TryDrawEntityVisual(spriteBatch, entityId, _tinyFont, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y), new Vector2Byte(1, 1));
    }

    /// <summary>The tile's floor, stretched to its tile -- static, so it belongs in the cached terrain texture. Walls are DrawStructures'.</summary>
    private void DrawFloor(SpriteBatch spriteBatch, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        if (_terrainCells.TryGetFloor(mapNodeX, mapNodeY, out var floor))
        {
            DrawVisual(spriteBatch, floor, _mediumFont, tileOrigin, new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y), footprintTiles: null, out _);
        }
    }

    /// <summary>A Blocking occupant, its health bar and loot badge, its charging badge and its dodge glow. The health bar and charging badge follow the drawn sprite (a tall sprite's head would otherwise cover them); the loot badge and dodge glow are about the footprint and stay on it.</summary>
    private void DrawPrimaryOccupant(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2Byte size, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var status = _mapView.GetStatus(entityId);

        TryDrawEntityVisual(spriteBatch, entityId, FontForSize(size.X), footprintTopLeft, footprintSize, size, out var drawnTop);
        var cueTop = SpritePlacementMath.CueTop(drawnTop, footprintTopLeft.Y);
        DrawEntityIcons(spriteBatch, unitRectangle, entityId, status, footprintTopLeft, footprintSize, cueTop);

        // Unlike DrawEntityIcons (health bar/loot badge), the charging badge must show for the
        // player too -- drawn here, directly, rather than inside DrawEntityIcons' own early
        // player-skip guard.
        DrawChargingBadge(spriteBatch, entityId, footprintTopLeft, footprintSize, cueTop);

        // Dodging is a temporary, brief immunity window (Combat Overhaul: Dodge) -- an inner-fade
        // glow framing the entity's own footprint on top of its sprite (the same GlowMode.InteriorFade
        // ring technique DrawSelectedTileGlow already uses), not a sprite-opacity fade: a translucent
        // sprite read as the entity vanishing outright rather than as a status cue (confirmed live).
        // GlowRenderer's own default rings top out at 50% alpha on the outermost ring, fading to 10%
        // on the innermost -- tuned for a tile-selection cue looked at at leisure, not a fast, brief
        // (as little as 0.5s at low Dexterity) combat status cue -- DodgingGlowAlphaMultiplier boosts
        // the outer rings toward fully opaque so the fade is still visible at a glance, confirmed live
        // as too subtle to notice at the un-boosted default.
        if (status.IsDodging)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, new Rectangle((int)footprintTopLeft.X, (int)footprintTopLeft.Y, (int)footprintSize.X, (int)footprintSize.Y), DodgingGlowColor, GlowMode.InteriorFade, DodgingGlowAlphaMultiplier);
        }
    }



    /// <summary>
    /// The LootBag-Red badge, shared by a corpse (DrawOccupants) and a live, still-Blocking
    /// container (DrawEntityIcons -- lootable while alive, unlike a corpse). Nothing when the
    /// entity carries no items; full color if its loot window has never been opened, grey once it
    /// has. Small and single-tile-sized, anchored to the top-right corner of the entity's own full
    /// footprint rather than its origin tile, so a multi-tile corpse gets its badge on its actual
    /// top-right tile.
    /// </summary>
    private void DrawLootBagBadge(SpriteBatch spriteBatch, LootBagState lootBag, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (lootBag == LootBagState.None || _lootBagSprite is not { } lootBagSprite)
        {
            return;
        }

        var tint = lootBag == LootBagState.Looted ? Color.Gray : Color.White;
        var badgeSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y) * LootBagBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + footprintSize.X - badgeSize.X, footprintTopLeft.Y);

        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, lootBagSprite, _badgeFont, string.Empty, tint, badgePosition, badgeSize, tint, outline: true);
    }

    /// <summary>
    /// While entityId is mid-windup on a Delayed action (PendingWindupComponent), draws
    /// that action's own sprite/glyph as a small badge centered above its footprint -- "put that
    /// action/item's sprite as a badge above their sprite on the map" (Combat Overhaul: Dodge,
    /// TODO.md). Positioned a full badge-height above cueTop -- the top of the drawn sprite, or the footprint's if that is higher -- so it
    /// never collides with DrawHealthBar's own bar, which sits inside the footprint's top edge.
    /// Item-charging badges are out of scope: no delayed/charging item activation exists anywhere
    /// today (every consumable is Immediate) -- revisit if one is ever introduced. Called directly
    /// from DrawPrimaryOccupant, not from DrawEntityIcons, since the player must see their own
    /// charging badge too and DrawEntityIcons deliberately skips the player entirely.
    /// </summary>
    private void DrawChargingBadge(SpriteBatch spriteBatch, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize, float cueTop)
    {
        if (!_mapView.TryGetChargingAction(entityId, out var action))
        {
            return;
        }

        var badgeSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y) * ChargingBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + (footprintSize.X - badgeSize.X) / 2f, cueTop - badgeSize.Y);

        SpriteView? actionSprite = action.SpriteName is { } spriteName && _spriteSheetService.Sprites.TryGetFirst(spriteName, out var icon) ? icon : null;
        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _labelRenderer, actionSprite, _badgeFont, action.Glyph, action.GlyphColor, badgePosition, badgeSize, Color.White, outline: true);
    }

    private void DrawEntityIcons(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, EntityStatusView status, Vector2 footprintTopLeft, Vector2 footprintSize, float cueTop)
    {
        if (entityId == _mapView.PlayerEntityId)
        {
            return;
        }

        if (status.HealthFraction is { } healthFraction)
        {
            DrawHealthBar(spriteBatch, unitRectangle, healthFraction, footprintTopLeft, footprintSize, cueTop);
        }

        // A container is lootable while alive, unlike a creature (only lootable once dead, see
        // DrawOccupants' corpse call to the same badge helper) -- gated on IsContainer, not
        // just "carries items," since a live goblin/fairy/player also carries its own inventory
        // stacks and must not show a loot-bag badge for those.
        if (status.IsContainer)
        {
            DrawLootBagBadge(spriteBatch, status.LootBag, footprintTopLeft, footprintSize);
        }
    }

    /// <summary>Thin bar at cueTop -- the top of the drawn sprite, or of the footprint if that is higher -- only drawn below full health (EntityStatusView.HealthFraction is null otherwise). Black backdrop doubles as the outline and the "missing health" portion; the fill rect insets 1px and its width (not the outline's) scales with the health fraction.</summary>
    private void DrawHealthBar(SpriteBatch spriteBatch, Texture2D unitRectangle, float healthFraction, Vector2 footprintTopLeft, Vector2 footprintSize, float cueTop)
    {
        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = cueTop;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var innerWidth = (int)((outerRectangle.Width - 2) * healthFraction);
        if (innerWidth > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, innerWidth, HealthBarHeightPixels - 2), HealthBarPalette.FractionColor(healthFraction));
        }
    }

    /// <summary>Medium/large/huge glyph font by an entity's TransformComponent.Size.X.</summary>
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
    /// drawn on it -- so MapDrawOrder plans every badge after every occupant, where no later
    /// row's tall sprite can paint over it.
    /// </summary>
    private void DrawLayerBadges(SpriteBatch spriteBatch, bool hasHigherLayer, bool hasLowerLayer, Vector2 tileOrigin)
    {
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
    /// <remarks>Invalidates the terrain caches afterward -- the visible tile count changes with zoom, the same cache-invalidation reasoning ChangeLayer/CenterCameraOn/CycleZoom each apply for their own trigger. No production caller yet (only MapWindowTests exercises this today) -- a candidate hook for a future zoom UI control (see the Minimap TODO item).</remarks>
    /// <param name="newZoomLevel">The zoom level to switch to.</param>
    public void UpdateZoomLevel(ZoomLevel newZoomLevel)
    {
        _camera.UpdateZoomLevel(newZoomLevel, ContentSize);
        InvalidateTerrainCaches();
    }

    private void CycleZoom(int direction)
    {
        _camera.CycleZoom(direction, ContentSize);
        InvalidateTerrainCaches();
    }

    /// <summary>Scrolls the camera by scrollChange tiles, keeping the terrain caches in sync.</summary>
    /// <remarks>MapCamera.UpdateScrollPosition clamps against the map edge and returns how much of the requested delta was actually applied, so a clamped scroll (e.g. dragging past the map edge) that didn't move the camera rebuilds nothing.</remarks>
    /// <param name="scrollChange">The requested scroll delta, in tiles.</param>
    public void UpdateScrollPosition(Point scrollChange)
    {
        var appliedDelta = _camera.UpdateScrollPosition(scrollChange);
        ApplyCameraScrollToCaches(appliedDelta);
    }

    /// <summary>
    /// Brings the per-visible-tile caches along with a scroll delta the camera has already applied --
    /// shared by every camera move (drag, drag-end snap, and camera-follow's own re-centre).
    /// </summary>
    /// <remarks>
    /// The two render-target caches hold a texture, not an array of resolved values, so they
    /// rebuild, but only when the camera genuinely moved. A no-op delta (the common case while
    /// standing still, and every step taken along a map edge) leaves both untouched.
    /// </remarks>
    /// <param name="appliedDelta">The scroll change the camera actually applied, in tiles.</param>
    private void ApplyCameraScrollToCaches(Point appliedDelta)
    {
        if (appliedDelta == Point.Zero)
        {
            return;
        }

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
    /// as last headers when there is at least one occupant group: a read-only name header (see ContextMenuOption.Header -- also the visual separator
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
            options.Add(ContextMenuOption.Header(structure.Name));
        }

        if (options.Count > 0 && _mapView.TryGetTerrain(mapPosition.X, mapPosition.Y, _mapViewState.CurrentMapLayer, out var terrain))
        {
            options.Add(ContextMenuOption.Header(terrain.Name));
        }

        if (GlobalState.IsAdminModeOn)
        {
            _adminContextMenuOptions.AddTileOptions(options, tilePosition);
        }

        if (options.Count > 0)
        {
            _contextMenuController.Open(new Vector2(mousePosition.X, mousePosition.Y), options);
        }
    }

    /// <summary>Appends one occupant's own group to the tile's stacked menu -- a read-only name header, then whatever options it offers.</summary>
    private void AddEntityGroup(List<ContextMenuOption> options, int entityId)
    {
        var interaction = _mapView.GetInteraction(entityId);
        options.Add(ContextMenuOption.Header(interaction.Name));

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
            var isLootReserved = interaction.IsLootReservedFromPlayer && !GlobalState.IsAdminModeOn;
            options.Add(new ContextMenuOption(isLootReserved ? "Loot (reserved)" : "Loot", null, IsAdjacentToPlayer(entityId) && !isLootReserved, () => onCorpseClicked.Invoke(entityId)));
        }

        if (isShop && !isDestroyed && OnShopClicked is { } onShopClicked)
        {
            options.Add(new ContextMenuOption("Shop", null, IsAdjacentToPlayer(entityId), () => onShopClicked.Invoke(entityId)));
        }

        options.Add(new ContextMenuOption("Inspect", null, !_playerActionGate.IsLocked, () => InspectEntity(entityId)));

        if (GlobalState.IsAdminModeOn)
        {
            _adminContextMenuOptions.AddEntityOptions(options, entityId);
        }
    }

    /// <summary>Details/Admin inspection's actual activation -- sets Detail or Admin mode (GlobalState.IsAdminModeOn) on the shared entityId (see MapViewState.InspectedEntityId), starts the global cooldown (the same shared ActionLockComponent lock movement/melee/consumables already use), and un-minimizes InspectionWindow. Only ever reached via the "Inspect" ContextMenuOption above, which already gates on the cooldown being clear -- no redundant re-check here, matching how "Loot" above trusts its own Enabled gate instead of re-checking adjacency.</summary>
    private void InspectEntity(int entityId)
    {
        _mapViewState.InspectionMode = GlobalState.IsAdminModeOn ? InspectionMode.Admin : InspectionMode.Detail;
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
            _camera.ResumeFollowingPlayer();
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

    /// <summary>
    /// Re-centres the camera and brings the per-visible-tile caches with it -- only when the
    /// camera actually moved.
    /// </summary>
    /// <remarks>
    /// Camera-follow calls this on every player step, so this is the hottest invalidation path in
    /// the window: a step that doesn't move the camera at all (against a map edge, or the follow
    /// already centred) costs nothing.
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