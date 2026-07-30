using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using FontStashSharp;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// Displays a scrollable/zoomable viewport onto a single MapLayer of the game map at a time.
/// </summary>
public sealed class MapWindow : Window
{
    private const int MaxTinyEntitiesDrawn = 9;
    private const int TinyGridDimension = 3;
    private static readonly Color UpLayerBadgeColor = Color.Blue;
    private static readonly Color DownLayerBadgeColor = new(101, 67, 33);

    private const float HealthBarWidthFraction = 0.9f;
    private const int HealthBarHeightPixels = 4;

    private readonly World _world;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly AbilityTargetingController _abilityTargeting;
    private readonly MapBackgroundCache _backgroundCache;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly PackedComponentPool<OccupancyComponent> _occupancyPool;
    private readonly PackedComponentPool<HealthComponent> _healthPool;
    private readonly TileRenderer _tileRenderer;
    private readonly GlyphRenderer _glyphRenderer;

    private static readonly Color TargetableTileBorderColor = Color.White;
    private static readonly Color HoveredTargetTileBorderColor = Color.Red;
    private const float TargetSelectionMaskAlpha = 0.5f;
    private static readonly Color MapBackgroundColor = new(40, 40, 40);

    private SpriteFontBase _mediumFont = null!;
    private SpriteFontBase _largeFont = null!;
    private SpriteFontBase _hugeFont = null!;
    private SpriteFontBase _tinyFont = null!;
    private SpriteFontBase _badgeFont = null!;

    private readonly int _tileDepth;

    /// <summary>True while the simulation is paused (Space, while this window holds focus -- see OnHotkeysAction). GameLoop.Update gates EcsContext.Update on this.</summary>
    public bool IsPaused { get; private set; }

    public MapWindow(
        FontService fontService,
        WindowService windowService,
        World world,
        MapViewState mapViewState,
        ComponentManager componentManager,
        AbilityCatalog abilityCatalog,
        TileRenderer tileRenderer,
        GlyphRenderer glyphRenderer) : base(fontService, windowService, glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapViewState);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(abilityCatalog);
        ArgumentNullException.ThrowIfNull(tileRenderer);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        _world = world;
        _mapViewState = mapViewState;
        _transformPool = componentManager.GetDirectPool<TransformComponent>();
        _glyphPool = componentManager.GetDirectPool<GlyphComponent>();
        _occupancyPool = componentManager.GetPackedPool<OccupancyComponent>();
        _healthPool = componentManager.GetPackedPool<HealthComponent>();
        _tileRenderer = tileRenderer;
        _glyphRenderer = glyphRenderer;

        _camera = new MapCamera(world);
        _abilityTargeting = new AbilityTargetingController(
            world,
            mapViewState,
            _camera,
            abilityCatalog,
            _transformPool,
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetMultiPool<HotkeyBindingComponent>(),
            componentManager.GetPackedPool<PendingAbilityActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>());
        _backgroundCache = new MapBackgroundCache(
            world,
            mapViewState,
            componentManager.GetDirectPool<BackgroundComponent>(),
            new MapTintGrid(componentManager, world.Map.Size),
            _camera);

        _tileDepth = _world.Map.Size.Z;
    }

    public override void Initialize()
    {
        base.Initialize();

        _mediumFont = FontService.GetFont(24);
        _largeFont = FontService.GetFont(72);
        _hugeFont = FontService.GetFont(108);
        _tinyFont = FontService.GetFont(6); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = FontService.GetFont(12); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        _camera.Initialize(ContentSize);
        _backgroundCache.Resize();

        SetCameraMapLayer(_mapViewState.CurrentMapLayer);

        if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform))
        {
            SnapCameraToPlayer(playerTransform.Position);
            _camera.LastKnownPlayerPosition = playerTransform.Position;
        }
        else
        {
            _backgroundCache.Reset();
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _abilityTargeting.Tick();

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
    }

    /// <summary>
    /// Delegates to AbilityTargetingController.UpdateHoveredTile -- kept as a method on MapWindow
    /// (internal, not private) since MapWindowTests exercises it directly the same way it does
    /// Window.HandleHotkeys, simulating a mouse position without a real OS cursor.
    /// </summary>
    internal void UpdateHoveredTile(Point mousePosition) => _abilityTargeting.UpdateHoveredTile(mousePosition, _contentState.AbsolutePosition);

    /// <summary>Read-only view of the armed ability's current hit-footprint -- see AbilityTargetingController.HoveredFootprint.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _abilityTargeting.HoveredFootprint;

    private void SnapCameraToPlayer(Vector3Int position)
    {
        SetCameraMapLayer(position.Z);
        CenterCameraOn(position);
    }

    /// <summary>The single place [0, _tileDepth - 1] clamping happens for MapViewState.CurrentMapLayer -- shared by ChangeLayer, SnapToPlayer, and Initialize's own re-clamp against whatever depth this particular Map turns out to have.</summary>
    private void SetCameraMapLayer(int layer)
    {
        _mapViewState.CurrentMapLayer = MathUtility.ClampInt(layer, 0, _tileDepth - 1);
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, _camera.TileColumns * _camera.CurrentTileSize.X, _camera.TileRows * _camera.CurrentTileSize.Y), MapBackgroundColor);

        _tileRenderer.DrawBackgrounds(spriteBatch, unitRectangle, _backgroundCache.Colors, _camera.TileColumns, _camera.TileRows, _camera.CurrentTileSize, _camera.RenderPixelOffset);
        DrawTargetingHighlights(spriteBatch, unitRectangle);
        DrawSelectedTileHighlight(spriteBatch, unitRectangle);
        DrawGlyphs(spriteBatch, unitRectangle);
    }

    /// <summary>Delegates to MapCamera.TileOrigin -- kept as a same-signature method here rather than inlined at every call site below.</summary>
    private Vector2 TileOrigin(int columnIndex, int rowIndex) => _camera.TileOrigin(columnIndex, rowIndex);

    /// <summary>
    /// Every tile the currently-armed ability could be aimed at (see MapViewState.TargetableTiles,
    /// computed once at arm time) -- a white border + 50% white mask for "targetable, not
    /// currently hovered," a red border + 50% red mask for whichever of those tiles the armed
    /// shape's hover-resolved footprint (see AbilityTargetingController.HoveredFootprint,
    /// recomputed every Update) actually covers right now. A separate, independent draw call
    /// from DrawSelectedTileHighlight below -- the two are conceptually distinct (ability
    /// targeting vs. the inspector's click-to-select), even though both now share the same
    /// border-plus-mask technique (see DrawMaskedTileHighlight).
    /// </summary>
    private void DrawTargetingHighlights(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.TargetableTiles is not { Count: > 0 } targetableTiles)
        {
            return;
        }

        foreach (var tile in targetableTiles)
        {
            var borderColor = _abilityTargeting.HoveredFootprintContains(tile) ? HoveredTargetTileBorderColor : TargetableTileBorderColor;
            DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor);
        }
    }

    private void DrawSelectedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        DrawMaskedTileHighlight(spriteBatch, unitRectangle, selectedPosition.X, selectedPosition.Y, Color.Gold);
    }

    /// <summary>
    /// Outer-border-then-refill-inner technique shared by every tile highlight -- the inspector's
    /// single-tile Gold selection and DrawTargetingHighlights' per-tile ability-targeting colors.
    /// The interior is refilled with the tile's actual background blended
    /// TargetSelectionMaskAlpha of the way toward borderColor (a "mask"), so the ring reads as a
    /// solid border while the tile's own contents still show through, just tinted.
    /// </summary>
    private void DrawMaskedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color borderColor)
    {
        var column = mapNodeX - _camera.CurrentScrollPosition.X;
        var row = mapNodeY - _camera.CurrentScrollPosition.Y;

        if (column < 0 || column >= _camera.TileColumns || row < 0 || row >= _camera.TileRows)
        {
            return;
        }

        var origin = TileOrigin(column, row);
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, borderColor);

        var innerRectangle = new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, _camera.InnerTileSize.X, _camera.InnerTileSize.Y);
        var maskedColor = Color.Lerp(_backgroundCache[column, row], borderColor, TargetSelectionMaskAlpha);
        spriteBatch.Draw(unitRectangle, innerRectangle, maskedColor);
    }

    /// <summary>
    /// Switches the single MapLayer this window renders (Page Up/Down -- see OnHotkeysAction),
    /// stored on MapViewState rather than locally so SelectionWindowContent
    /// can scope the inspector to the same layer this window is actually showing. Background
    /// depends on the current layer's terrain (see MapBackgroundCache), so the cache must
    /// be rebuilt on every change, the same as a zoom-level change.
    /// </summary>
    public void ChangeLayer(int delta)
    {
        SetCameraMapLayer(_mapViewState.CurrentMapLayer + delta);
        _backgroundCache.Reset();
    }

    private void DrawGlyphs(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;
        var occupantsByPosition = BuildOccupantsByPosition();
        var terrainLayer = Map.TerrainLayerFor(currentMapLayer);

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
                occupantsByPosition.TryGetValue(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer), out var occupantsHere);

                DrawTerrainGlyph(spriteBatch, terrainLayer, mapNodeX, mapNodeY, tileOrigin);
                DrawTinyGrid(spriteBatch, occupantsHere, tileOrigin);
                DrawPrimaryOccupant(spriteBatch, unitRectangle, currentMapLayer, mapNodeX, mapNodeY, columnIndex, rowIndex);
                DrawPhasingGlyphs(spriteBatch, occupantsHere, tileOrigin);
                DrawLayerBadges(spriteBatch, occupantsByPosition, currentMapLayer, mapNodeX, mapNodeY, tileOrigin);
            }
        }
    }

    /// <summary>
    /// Buckets every Tiny/Phasing entity by position, once per frame rather than per tile --
    /// deliberately a fresh scan every draw rather than an index kept in sync with movement,
    /// since this population (ghosts/insects) is expected to be small and this view is
    /// temporary pending a full UI rework.
    /// </summary>
    private Dictionary<Vector3Int, List<int>> BuildOccupantsByPosition()
    {
        var occupantsByPosition = new Dictionary<Vector3Int, List<int>>();

        foreach (var entityId in _occupancyPool.EntityIds)
        {
            if (!_transformPool.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            var position = transformComponent.Position;
            if (!occupantsByPosition.TryGetValue(position, out var occupants))
            {
                occupants = [];
                occupantsByPosition[position] = occupants;
            }

            occupants.Add(entityId);
        }

        return occupantsByPosition;
    }

    private void DrawTerrainGlyph(SpriteBatch spriteBatch, TerrainLayer? terrainLayer, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        if (terrainLayer is not { } layer)
        {
            return;
        }

        var terrainEntityId = _world.Map.GetTerrainEntityId(mapNodeX, mapNodeY, layer);
        if (terrainEntityId == -1 || !_glyphPool.TryGetReadonly(terrainEntityId, out var glyphComponent))
        {
            return;
        }

        var footprintSize = new Vector2(_camera.CurrentTileSize.X, _camera.CurrentTileSize.Y); // Terrain is always 1x1.
        _glyphRenderer.DrawCentered(spriteBatch, _mediumFont, glyphComponent.Glyph, tileOrigin, footprintSize, glyphComponent.GlyphColor);
    }

    /// <summary>Up to 9 Tiny entities in a 3x3 sub-grid, each &lt;= 1/3 tile size; extras beyond 9 are simply not drawn.</summary>
    private void DrawTinyGrid(SpriteBatch spriteBatch, List<int>? occupants, Vector2 tileOrigin)
    {
        if (occupants is null)
        {
            return;
        }

        var subCellSize = new Point(_camera.CurrentTileSize.X / TinyGridDimension, _camera.CurrentTileSize.Y / TinyGridDimension);
        var drawnCount = 0;

        foreach (var entityId in occupants)
        {
            if (drawnCount >= MaxTinyEntitiesDrawn)
            {
                break;
            }

            if (!_occupancyPool.GetReadonly(entityId).IsTiny || !_glyphPool.TryGetReadonly(entityId, out var glyphComponent))
            {
                continue;
            }

            var subColumn = drawnCount % TinyGridDimension;
            var subRow = drawnCount / TinyGridDimension;
            var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

            _glyphRenderer.DrawCentered(spriteBatch, _tinyFont, glyphComponent.Glyph, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y), glyphComponent.GlyphColor);
            drawnCount++;
        }
    }

    private void DrawPrimaryOccupant(SpriteBatch spriteBatch, Texture2D unitRectangle, int currentMapLayer, int mapNodeX, int mapNodeY, int columnIndex, int rowIndex)
    {
        var entityId = _world.Map.GetEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
        if (entityId == -1)
        {
            return;
        }

        if (!_glyphPool.TryGetReadonly(entityId, out var glyphComponent) || !_transformPool.TryGetReadonly(entityId, out var transformComponent))
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

        _glyphRenderer.DrawCentered(spriteBatch, FontForSize(transformComponent.Size.X), glyphComponent.Glyph, footprintTopLeft, footprintSize, glyphComponent.GlyphColor);
        DrawEntityIcons(spriteBatch, unitRectangle, entityId, footprintTopLeft, footprintSize);
    }

    /// <summary>Skips the player entirely -- the top-right HUD health bar (see PlayerHealthBarContent) covers the player, so no per-tile icon here, now or for anything added to this method later, should duplicate it.</summary>
    private void DrawEntityIcons(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (entityId == _world.PlayerEntityId)
        {
            return;
        }

        DrawHealthBar(spriteBatch, unitRectangle, entityId, footprintTopLeft, footprintSize);
    }

    /// <summary>Thin bar at the top of the entity's own footprint, above its glyph, hidden at full health. Black backdrop doubles as the outline and the "missing health" portion; the fill rect insets 1px and its width (not the outline's) scales with the health fraction.</summary>
    private void DrawHealthBar(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        if (!_healthPool.TryGetReadonly(entityId, out var health) || health.MaximumHealth <= 0 || health.CurrentHealth >= health.MaximumHealth)
        {
            return;
        }

        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = footprintTopLeft.Y;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var healthFraction = (float)health.CurrentHealth / health.MaximumHealth;
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

    /// <summary>Every Phasing entity here draws at 50% alpha, stacked -- SpriteBatchRenderer already begins with BlendState.AlphaBlend.</summary>
    private void DrawPhasingGlyphs(SpriteBatch spriteBatch, List<int>? occupants, Vector2 tileOrigin)
    {
        if (occupants is null)
        {
            return;
        }

        foreach (var entityId in occupants)
        {
            if (!_occupancyPool.GetReadonly(entityId).IsPhasing ||
                !_glyphPool.TryGetReadonly(entityId, out var glyphComponent) ||
                !_transformPool.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            var footprintSize = new Vector2(transformComponent.Size.X * _camera.CurrentTileSize.X, transformComponent.Size.Y * _camera.CurrentTileSize.Y);

            _glyphRenderer.DrawCentered(spriteBatch, FontForSize(transformComponent.Size.X), glyphComponent.Glyph, tileOrigin, footprintSize, glyphComponent.GlyphColor * 0.5f);
        }
    }

    /// <summary>
    /// Blue up-arrow (top-right) if any layer above the current one is occupied; brown
    /// down-arrow (bottom-right) if any layer below is. A tile-level badge -- unlike
    /// DrawEntityIcons, this describes the tile's other layers, not the Blocking occupant
    /// drawn on it.
    /// </summary>
    private void DrawLayerBadges(SpriteBatch spriteBatch, Dictionary<Vector3Int, List<int>> occupantsByPosition, int currentMapLayer, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        var hasHigherLayer = false;
        for (var layer = currentMapLayer + 1; layer < _tileDepth; layer++)
        {
            if (IsLayerOccupied(occupantsByPosition, mapNodeX, mapNodeY, layer))
            {
                hasHigherLayer = true;
                break;
            }
        }

        var hasLowerLayer = false;
        for (var layer = currentMapLayer - 1; layer >= 0; layer--)
        {
            if (IsLayerOccupied(occupantsByPosition, mapNodeX, mapNodeY, layer))
            {
                hasLowerLayer = true;
                break;
            }
        }

        if (hasHigherLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + _camera.CurrentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y);
            _glyphRenderer.Draw(spriteBatch, _badgeFont, "^", drawPosition, UpLayerBadgeColor);
        }

        if (hasLowerLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + _camera.CurrentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y + _camera.CurrentTileSize.Y - _badgeFont.LineHeight);
            _glyphRenderer.Draw(spriteBatch, _badgeFont, "v", drawPosition, DownLayerBadgeColor);
        }
    }

    /// <summary>"Occupied" counts a Blocking entity in Map's slot exactly the same as a Tiny/Phasing entity tracked only in occupantsByPosition.</summary>
    private bool IsLayerOccupied(Dictionary<Vector3Int, List<int>> occupantsByPosition, int mapNodeX, int mapNodeY, int layer)
    {
        if (_world.Map.GetEntityId(new Vector3Int(mapNodeX, mapNodeY, layer)) != -1)
        {
            return true;
        }

        return occupantsByPosition.ContainsKey(new Vector3Int(mapNodeX, mapNodeY, layer));
    }

    public void UpdateZoomLevel(ZoomLevel newZoomLevel)
    {
        _camera.UpdateZoomLevel(newZoomLevel, ContentSize);
        _backgroundCache.Resize();
        _backgroundCache.Reset();
    }

    private void CycleZoom(int direction)
    {
        _camera.CycleZoom(direction, ContentSize);
        _backgroundCache.Resize();
        _backgroundCache.Reset();
    }

    public void UpdateScrollPosition(Point scrollChange)
    {
        var appliedDelta = _camera.UpdateScrollPosition(scrollChange);
        _backgroundCache.ApplyScroll(appliedDelta.X, appliedDelta.Y);
    }

    public void SelectMapNodes(Point mousePosition)
    {
        if (_camera.TryGetHoveredMapPosition(mousePosition, _contentState.AbsolutePosition, out var mapPosition))
        {
            _mapViewState.SelectedMapNodePosition = mapPosition;
        }
    }

    /// <summary>
    /// A left-click confirms the armed ability's activation if an ability is armed, falling
    /// back to the ordinary inspector click-select otherwise -- an armed ability's target
    /// selection takes over the click entirely while it's active, matching how the outline
    /// describes left-click as the universal "activate" gesture once something is armed.
    /// </summary>
    protected override void OnContentClickAction(Point mousePosition)
    {
        if (_mapViewState.ArmedAbilityId is { } abilityId)
        {
            _abilityTargeting.TryConfirmActivation(mousePosition, _contentState.AbsolutePosition, abilityId);
            return;
        }

        SelectMapNodes(mousePosition);
    }

    protected override void OnRightClickTapAction() => _abilityTargeting.CancelArmedOrPendingAction();

    protected override void OnEscapeAction() => _abilityTargeting.CancelArmedOrPendingAction();

    /// <summary>The map's own hotkeys -- only invoked while this window holds focus (see GameInputController.RouteHotkeysToFocusedWindow).</summary>
    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Space))
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

        _abilityTargeting.HandleHotkeys(keyboardState, previousKeyboardState);
    }

    private void CenterCameraOn(Vector3Int position)
    {
        _camera.CenterCameraOn(position);
        _backgroundCache.Reset();
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