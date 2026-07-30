using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using FontStashSharp;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
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

    private const int FramesPerPlayerMove = 15;

    private readonly World _world;
    private readonly MapViewState _mapViewState;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly DirectComponentPool<BackgroundComponent> _backgroundPool;
    private readonly PackedComponentPool<OccupancyComponent> _occupancyPool;
    private readonly PackedComponentPool<MovementComponent> _movementPool;
    private readonly PackedComponentPool<HealthComponent> _healthPool;
    private readonly AbilityCatalog _abilityCatalog;
    private readonly MultiComponentPool<HotkeyBindingComponent> _hotkeyBindings;
    private readonly PackedComponentPool<PendingAbilityActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly TileRenderer _tileRenderer;
    private readonly GlyphRenderer _glyphRenderer;

    /// <summary>~300ms at 60fps -- a second press of the same slot within this many frames of the first is a double-tap (see HandleHotkeySlotPress), not two independent arm/disarm presses.</summary>
    private const int DoubleTapWindowFrames = 18;

    /// <summary>Frame-counted (see ActionLockComponent's own doc comment for why this codebase denominates timers in frames, not wall-clock time), incremented once per Update call -- OnHotkeysAction has no GameTime of its own to measure elapsed time against.</summary>
    private int _frameCounter;

    private readonly Dictionary<HotkeySlot, int> _lastHotkeyPressFrameBySlot = [];

    // Reused across calls (see TargetShapeResolver's own doc comment on why Resolve writes into
    // a caller-owned buffer instead of allocating).
    private readonly List<Vector3Int> _candidateTilesBuffer = [];
    private readonly List<Vector3Int> _occupiedCandidateTilesBuffer = [];
    private readonly List<Vector3Int> _finalTargetTilesBuffer = [];

    /// <summary>The armed ability's actual hit-footprint at the current hover position, recomputed every Update (see UpdateHoveredTile) -- MapWindow-local rendering state, not shared via MapViewState since nothing else consumes it.</summary>
    private readonly List<Vector3Int> _hoveredFootprintBuffer = [];

    /// <summary>Read-only view of _hoveredFootprintBuffer for tests -- same internal-for-test-visibility pattern as GameInputController.CurrentCursor/DragDelta.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _hoveredFootprintBuffer;

    private static readonly Color TargetableTileColor = Color.CornflowerBlue * 0.6f;
    private static readonly Color HoveredTargetTileColor = Color.OrangeRed * 0.75f;

    // Normalizes DistanceFalloff.ValueAtDistance(source.Strength, distance) into a 0-1 Color.Lerp
    // factor -- a source at distance 0 with Strength >= this shows its TintColor at full strength.
    private const int MaxTintStrength = 8;

    /// <summary>
    /// Precomputed once (constructor), not scanned live per cache rebuild: every visible-tile
    /// cache rebuild used to iterate every StatusEffectAuraSourceComponent in the whole game
    /// (fine for a handful of sources, catastrophic with TestMapBuilder's real lava density --
    /// tens of thousands of sources scanned on every player move tanked FPS to ~1). Sparse
    /// (only cells actually within some source's radius have an entry) since
    /// StatusEffectAuraSourceComponent is terrain-anchored and static once placed -- see
    /// BuildTintGrid. Where multiple sources overlap a cell, their colors are blended by a
    /// falloff-weighted average rather than the sequential per-source Color.Lerp chain an
    /// earlier, unscalable version of this used.
    /// </summary>
    private readonly Dictionary<int, (Color Color, float Factor)> _tintGrid;

    private int _playerMoveCooldownFrames;

    private bool _cameraFollowsPlayer = true;

    private Vector3Int _lastKnownPlayerPosition;

    private Point _rightDragStartScrollPosition;

    private Vector2 _renderPixelOffset;

    private SpriteFontBase _mediumFont = null!;
    private SpriteFontBase _largeFont = null!;
    private SpriteFontBase _hugeFont = null!;
    private SpriteFontBase _tinyFont = null!;
    private SpriteFontBase _badgeFont = null!;

    private Point _currentScrollPosition;
    private Point _maxScrollPosition;

    private Point _currentTileSize;
    private Point _innerTileSize;
    private int _tileColumns;
    private int _tileRows;
    private readonly int _tileDepth;

    private const int BaseTileSizePixels = 36;

    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;
    private static readonly Dictionary<ZoomLevel, Point> TileSizesByZoomLevel = new()
    {
        [ZoomLevel.Team] = new Point(BaseTileSizePixels, BaseTileSizePixels),
        [ZoomLevel.Neighborhood] = new Point(BaseTileSizePixels / 2, BaseTileSizePixels / 2),
        [ZoomLevel.Borough] = new Point(BaseTileSizePixels / 4, BaseTileSizePixels / 4),
    };

    private Color[] _backgroundColorCache = [];

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
        _backgroundPool = componentManager.GetDirectPool<BackgroundComponent>();
        _occupancyPool = componentManager.GetPackedPool<OccupancyComponent>();
        _movementPool = componentManager.GetPackedPool<MovementComponent>();
        _healthPool = componentManager.GetPackedPool<HealthComponent>();
        _abilityCatalog = abilityCatalog;
        _hotkeyBindings = componentManager.GetMultiPool<HotkeyBindingComponent>();
        _pendingActivations = componentManager.GetPackedPool<PendingAbilityActivationComponent>();
        _pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();
        _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
        _tileRenderer = tileRenderer;
        _glyphRenderer = glyphRenderer;

        _tileDepth = _world.Map.Size.Z;

        _tintGrid = BuildTintGrid(componentManager, _world.Map.Size);
    }

    /// <summary>
    /// One-time scatter over every StatusEffectAuraSourceComponent (see this class's own doc
    /// comment on _tintGrid for why this must be precomputed, not scanned per rebuild).
    /// Accumulates a falloff-weighted RGB sum plus total weight per affected cell, then
    /// finalizes each into a single (blended Color, 0-1 factor) pair -- multiple overlapping
    /// sources of different colors blend proportionally to how strongly each reaches that
    /// cell, rather than requiring a specific application order. Scatters through
    /// DistanceFalloff.ScatterManhattan -- the same falloff shape StatusEffectAuraSystem/
    /// AuraGrid use on the gameplay side, defined in exactly one place, so glow always
    /// visually matches actual aura reach (both read the same AuraAndGlowStrength).
    /// </summary>
    private static Dictionary<int, (Color Color, float Factor)> BuildTintGrid(ComponentManager componentManager, Vector3Int mapSize)
    {
        var auraSourcePool = componentManager.GetPackedPool<StatusEffectAuraSourceComponent>();
        var transformPool = componentManager.GetDirectPool<TransformComponent>();

        var weightedSums = new Dictionary<int, (float R, float G, float B, float Weight)>();

        var entityIds = auraSourcePool.EntityIds;
        var auraSources = auraSourcePool.Components;
        for (var i = 0; i < entityIds.Length; i++)
        {
            if (!transformPool.TryGetReadonly(entityIds[i], out var transform))
            {
                continue;
            }

            var auraSource = auraSources[i];
            var sourcePosition = transform.Position;

            DistanceFalloff.ScatterManhattan(sourcePosition, auraSource.AuraAndGlowStrength, mapSize, (cellPosition, weight) =>
            {
                var index = cellPosition.FlatIndex(mapSize);
                weightedSums.TryGetValue(index, out var accumulated);
                weightedSums[index] = (
                    accumulated.R + auraSource.GlowColor.R * weight,
                    accumulated.G + auraSource.GlowColor.G * weight,
                    accumulated.B + auraSource.GlowColor.B * weight,
                    accumulated.Weight + weight);
            });
        }

        var tintGrid = new Dictionary<int, (Color Color, float Factor)>(weightedSums.Count);
        foreach (var (index, accumulated) in weightedSums)
        {
            var blendedColor = new Color(
                (byte)(accumulated.R / accumulated.Weight),
                (byte)(accumulated.G / accumulated.Weight),
                (byte)(accumulated.B / accumulated.Weight));
            var factor = MathUtility.ClampInt((int)accumulated.Weight, 0, MaxTintStrength) / (float)MaxTintStrength;

            tintGrid[index] = (blendedColor, factor);
        }

        return tintGrid;
    }

    private int TintGridIndex(int mapNodeX, int mapNodeY, int mapLayer) =>
        new Vector3Int(mapNodeX, mapNodeY, mapLayer).FlatIndex(_world.Map.Size);

    public override void Initialize()
    {
        base.Initialize();

        _mediumFont = FontService.GetFont(24);
        _largeFont = FontService.GetFont(72);
        _hugeFont = FontService.GetFont(108);
        _tinyFont = FontService.GetFont(6); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = FontService.GetFont(12); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        UpdateTileSizes();
        UpdateMaxScrollPosition();

        SetCameraMapLayer(_mapViewState.CurrentMapLayer);

        if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform))
        {
            SnapCameraToPlayer(playerTransform.Position);
            _lastKnownPlayerPosition = playerTransform.Position;
        }
        else
        {
            ResetBackgroundColorCache();
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _frameCounter++;

        if (_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform) && playerTransform.Position != _lastKnownPlayerPosition)
        {
            _lastKnownPlayerPosition = playerTransform.Position;

            if (_cameraFollowsPlayer)
            {
                CenterCameraOn(playerTransform.Position);
            }
        }

        var mouseState = Mouse.GetState();
        UpdateHoveredTile(new Point(mouseState.X, mouseState.Y));
    }

    /// <summary>
    /// While an ability is armed, tracks which map tile the mouse is currently over (on the
    /// player's own Z layer, not necessarily whatever layer the camera happens to be showing)
    /// and, if so, recomputes the armed ability's actual hit-footprint from that hover position
    /// via TargetShapeResolver -- this is what lets Burst/Cone/Line's highlighted tiles move
    /// with the cursor instead of staying fixed at arm time. Takes the mouse position explicitly
    /// (Update reads Mouse.GetState() -- the same static FNA API GameInputController.Update
    /// itself reads -- and passes it in) rather than reading it here directly, so tests can
    /// simulate a mouse position without a real OS cursor, the same way HandleHotkeys takes an
    /// explicit KeyboardState instead of reading Keyboard.GetState() itself. internal, not
    /// private, for that same test-visibility reason (see Window.HandleHotkeys).
    /// </summary>
    internal void UpdateHoveredTile(Point mousePosition)
    {
        _hoveredFootprintBuffer.Clear();

        if (_mapViewState.ArmedAbilityId is not { } abilityId)
        {
            _mapViewState.HoveredTile = null;
            return;
        }

        if (!TryGetHoveredMapPosition(mousePosition, out var hoveredColumnRow) ||
            !_transformPool.TryGetReadonly(_world.PlayerEntityId, out var playerTransform))
        {
            _mapViewState.HoveredTile = null;
            return;
        }

        var hoveredTile = new Vector3Int(hoveredColumnRow.X, hoveredColumnRow.Y, playerTransform.Position.Z);
        _mapViewState.HoveredTile = hoveredTile;

        if (_abilityCatalog.TryGet(abilityId, out var ability))
        {
            TargetShapeResolver.Resolve(ability.Targeting.Shape, playerTransform.Position, hoveredTile, ability.Targeting.Range, ability.Targeting.AreaSize, _world.Map.Size, _hoveredFootprintBuffer);
        }
    }

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
        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, _tileColumns * _currentTileSize.X, _tileRows * _currentTileSize.Y), Color.DarkGray);

        _tileRenderer.DrawBackgrounds(spriteBatch, unitRectangle, _backgroundColorCache, _tileColumns, _tileRows, _currentTileSize, _renderPixelOffset);
        DrawTargetingHighlights(spriteBatch, unitRectangle);
        DrawSelectedTileHighlight(spriteBatch, unitRectangle);
        DrawGlyphs(spriteBatch, unitRectangle);
    }

    /// <summary>
    /// The screen position of a tile's top-left corner, given its column/row within the
    /// visible viewport -- shared by every draw method below so _renderPixelOffset's smooth
    /// drag shift (see OnRightDragAction) only ever needs applying in one place.
    /// </summary>
    private Vector2 TileOrigin(int columnIndex, int rowIndex) =>
        new Vector2(columnIndex * _currentTileSize.X, rowIndex * _currentTileSize.Y) - _renderPixelOffset;

    /// <summary>
    /// Every tile the currently-armed ability could be aimed at (see MapViewState.TargetableTiles,
    /// computed once at arm time) -- one color for "targetable, not currently hovered," a second,
    /// distinct color for whichever of those tiles the armed shape's hover-resolved footprint
    /// (_hoveredFootprintBuffer, recomputed every Update -- see UpdateHoveredTile) actually covers
    /// right now. A separate, independent draw call from DrawSelectedTileHighlight below -- the
    /// two are conceptually distinct (ability targeting vs. the inspector's click-to-select) even
    /// though they share the same low-level tile-rectangle technique (see DrawTileHighlight).
    /// </summary>
    private void DrawTargetingHighlights(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.TargetableTiles is not { Count: > 0 } targetableTiles)
        {
            return;
        }

        foreach (var tile in targetableTiles)
        {
            var color = _hoveredFootprintBuffer.Contains(tile) ? HoveredTargetTileColor : TargetableTileColor;
            DrawTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, color);
        }
    }

    private void DrawSelectedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        DrawTileHighlight(spriteBatch, unitRectangle, selectedPosition.X, selectedPosition.Y, Color.Gold);
    }

    /// <summary>Outer-border-then-refill-inner technique shared by every tile highlight -- DrawSelectedTileHighlight's single-tile Gold inspector highlight, and DrawTargetingHighlights' per-tile ability-targeting colors.</summary>
    private void DrawTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color color)
    {
        var column = mapNodeX - _currentScrollPosition.X;
        var row = mapNodeY - _currentScrollPosition.Y;

        if (column < 0 || column >= _tileColumns || row < 0 || row >= _tileRows)
        {
            return;
        }

        var origin = TileOrigin(column, row);
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, _currentTileSize.X, _currentTileSize.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, color);

        var innerRectangle = new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, _innerTileSize.X, _innerTileSize.Y);
        spriteBatch.Draw(unitRectangle, innerRectangle, _backgroundColorCache[column + row * _tileColumns]);
    }

    /// <summary>
    /// Switches the single MapLayer this window renders (Page Up/Down -- see OnHotkeysAction),
    /// stored on MapViewState rather than locally so SelectionWindowContent
    /// can scope the inspector to the same layer this window is actually showing. Background
    /// depends on the current layer's terrain (see ResolveBackgroundColor), so the cache must
    /// be rebuilt on every change, the same as a zoom-level change.
    /// </summary>
    public void ChangeLayer(int delta)
    {
        SetCameraMapLayer(_mapViewState.CurrentMapLayer + delta);
        ResetBackgroundColorCache();
    }

    private void DrawGlyphs(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;
        var occupantsByPosition = BuildOccupantsByPosition();
        var terrainLayer = Map.TerrainLayerFor(currentMapLayer);

        for (var columnIndex = 0; columnIndex < _tileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _tileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _currentScrollPosition.X;
                var mapNodeY = rowIndex + _currentScrollPosition.Y;

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

        var footprintSize = new Vector2(_currentTileSize.X, _currentTileSize.Y); // Terrain is always 1x1.
        _glyphRenderer.DrawCentered(spriteBatch, _mediumFont, glyphComponent.Glyph, tileOrigin, footprintSize, glyphComponent.GlyphColor);
    }

    /// <summary>Up to 9 Tiny entities in a 3x3 sub-grid, each &lt;= 1/3 tile size; extras beyond 9 are simply not drawn.</summary>
    private void DrawTinyGrid(SpriteBatch spriteBatch, List<int>? occupants, Vector2 tileOrigin)
    {
        if (occupants is null)
        {
            return;
        }

        var subCellSize = new Point(_currentTileSize.X / TinyGridDimension, _currentTileSize.Y / TinyGridDimension);
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
        var footprintSize = new Vector2(transformComponent.Size.X * _currentTileSize.X, transformComponent.Size.Y * _currentTileSize.Y);

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

            var footprintSize = new Vector2(transformComponent.Size.X * _currentTileSize.X, transformComponent.Size.Y * _currentTileSize.Y);

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
            var drawPosition = new Vector2(tileOrigin.X + _currentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y);
            _glyphRenderer.Draw(spriteBatch, _badgeFont, "^", drawPosition, UpLayerBadgeColor);
        }

        if (hasLowerLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + _currentTileSize.X - _badgeFont.LineHeight, tileOrigin.Y + _currentTileSize.Y - _badgeFont.LineHeight);
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

    private void UpdateMaxScrollPosition()
    {
        // Never negative: a map smaller than the viewport has nowhere to scroll, not a
        // negative amount to scroll -- ClampInt(current, 0, max) requires max >= 0.
        _maxScrollPosition = new Point(
            System.Math.Max(0, _world.Map.Size.X - _tileColumns),
            System.Math.Max(0, _world.Map.Size.Y - _tileRows));
    }

    public void UpdateZoomLevel(ZoomLevel newZoomLevel)
    {
        _currentZoomLevel = newZoomLevel;
        UpdateTileSizes();

        // Zooming changes how many tiles are visible, so the max scroll bound (computed
        // from the visible tile count) is now stale too -- and the current scroll position,
        // valid under the old bound, may now exceed the new one (e.g. zooming out after
        // scrolling far while zoomed in) and needs re-clamping before the cache rebuilds.
        UpdateMaxScrollPosition();
        _currentScrollPosition = new Point(
            MathUtility.ClampInt(_currentScrollPosition.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(_currentScrollPosition.Y, 0, _maxScrollPosition.Y));

        // A zoom mid-drag would otherwise leave a stale smooth-scroll offset sized for the old
        // tile size shifting the newly-resized grid.
        _renderPixelOffset = Vector2.Zero;

        ResetBackgroundColorCache();
    }

    public void UpdateScrollPosition(Point scrollChange)
    {
        var previousScrollPosition = _currentScrollPosition;

        _currentScrollPosition = new Point(
            MathUtility.ClampInt(_currentScrollPosition.X + scrollChange.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(_currentScrollPosition.Y + scrollChange.Y, 0, _maxScrollPosition.Y));

        IncrementalUpdateBackgroundColorCache(
            _currentScrollPosition.X - previousScrollPosition.X,
            _currentScrollPosition.Y - previousScrollPosition.Y);
    }

    private void UpdateTileSizes()
    {
        _currentTileSize = TileSizesByZoomLevel[_currentZoomLevel];
        _innerTileSize = new Point(_currentTileSize.X - 2, _currentTileSize.Y - 2);

        // +2 to account for partial tile rendering and scrolling jitter
        _tileColumns = (int)System.Math.Floor(ContentSize.X / _currentTileSize.X) + 2;
        _tileRows = (int)System.Math.Floor(ContentSize.Y / _currentTileSize.Y) + 2;

        _backgroundColorCache = new Color[_tileColumns * _tileRows];
    }

    private void ResetBackgroundColorCache()
    {
        for (var column = 0; column < _tileColumns; column++)
        {
            for (var row = 0; row < _tileRows; row++)
            {
                var mapNodeX = column + _currentScrollPosition.X;
                var mapNodeY = row + _currentScrollPosition.Y;
                _backgroundColorCache[column + row * _tileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
            }
        }
    }

    /// <summary>
    /// Shifts already-known cells into their new positions and only re-resolves the
    /// newly-exposed columns/rows, instead of recomputing the whole visible grid on every
    /// scroll step.
    /// </summary>
    private void IncrementalUpdateBackgroundColorCache(int scrollDeltaX, int scrollDeltaY)
    {
        if (scrollDeltaX == 0 && scrollDeltaY == 0)
        {
            return;
        }

        // A delta at least as large as the viewport leaves nothing to shift -- every cell is
        // newly exposed, so a full rebuild is both correct and cheaper than shifting nothing
        // and then filling everything. The fill loops below also assume the delta is smaller
        // than the viewport in the axis they fill (e.g. "fill the last scrollDeltaX columns"),
        // so without this guard a big-enough jump (a large map with a small enough viewport
        // that a single scroll/drag can exceed it) indexes the cache array out of bounds.
        if (System.Math.Abs(scrollDeltaX) >= _tileColumns || System.Math.Abs(scrollDeltaY) >= _tileRows)
        {
            ResetBackgroundColorCache();
            return;
        }

        var shiftedColorCache = new Color[_tileColumns * _tileRows];

        for (var columnIndex = 0; columnIndex < _tileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _tileRows; rowIndex++)
            {
                var scrollColumn = columnIndex + scrollDeltaX;
                var scrollRow = rowIndex + scrollDeltaY;

                if (scrollColumn >= 0 && scrollColumn < _tileColumns && scrollRow >= 0 && scrollRow < _tileRows)
                {
                    shiftedColorCache[columnIndex + rowIndex * _tileColumns] = _backgroundColorCache[scrollColumn + scrollRow * _tileColumns];
                }
            }
        }

        _backgroundColorCache = shiftedColorCache;

        if (scrollDeltaX > 0)
        {
            for (var column = _tileColumns - scrollDeltaX; column < _tileColumns; column++)
            {
                FillBackgroundColumn(column);
            }
        }
        else if (scrollDeltaX < 0)
        {
            for (var column = 0; column < -scrollDeltaX; column++)
            {
                FillBackgroundColumn(column);
            }
        }

        if (scrollDeltaY > 0)
        {
            for (var row = _tileRows - scrollDeltaY; row < _tileRows; row++)
            {
                FillBackgroundRow(row);
            }
        }
        else if (scrollDeltaY < 0)
        {
            for (var row = 0; row < -scrollDeltaY; row++)
            {
                FillBackgroundRow(row);
            }
        }
    }

    private void FillBackgroundColumn(int column)
    {
        var mapNodeX = column + _currentScrollPosition.X;
        for (var row = 0; row < _tileRows; row++)
        {
            var mapNodeY = row + _currentScrollPosition.Y;
            _backgroundColorCache[column + row * _tileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
        }
    }

    private void FillBackgroundRow(int row)
    {
        var mapNodeY = row + _currentScrollPosition.Y;
        for (var column = 0; column < _tileColumns; column++)
        {
            var mapNodeX = column + _currentScrollPosition.X;
            _backgroundColorCache[column + row * _tileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
        }
    }

    /// <summary>
    /// The current layer's Blocking occupant (if it has its own BackgroundComponent) takes
    /// priority over the terrain beneath it -- a creature's background should read as that
    /// creature, not as whatever floor it happens to be standing on. Falls back to terrain
    /// (see Map.TerrainLayerFor -- Flying has none) when the occupant has no background of
    /// its own, or there's no occupant at all.
    /// </summary>
    private Color ResolveBackgroundColor(int mapNodeX, int mapNodeY)
    {
        if (!_world.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)))
        {
            return Color.Black;
        }

        var currentMapLayer = _mapViewState.CurrentMapLayer;

        var occupantEntityId = _world.Map.GetEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
        if (occupantEntityId != -1 && _backgroundPool.TryGetReadonly(occupantEntityId, out var occupantBackground))
        {
            return occupantBackground.BackgroundColor;
        }

        Color baseColor;
        if (Map.TerrainLayerFor(currentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = _world.Map.GetTerrainEntityId(mapNodeX, mapNodeY, terrainLayer);
            baseColor = terrainEntityId != -1 && _backgroundPool.TryGetReadonly(terrainEntityId, out var terrainBackground)
                ? terrainBackground.BackgroundColor
                : Color.White;
        }
        else
        {
            baseColor = Color.White;
        }

        // O(1) precomputed-grid lookup (see _tintGrid's own doc comment) -- not a live scan.
        return _tintGrid.TryGetValue(TintGridIndex(mapNodeX, mapNodeY, currentMapLayer), out var tint)
            ? Color.Lerp(baseColor, tint.Color, tint.Factor)
            : baseColor;
    }

    public void SelectMapNodes(Point mousePosition)
    {
        if (TryGetHoveredMapPosition(mousePosition, out var mapPosition))
        {
            _mapViewState.SelectedMapNodePosition = mapPosition;
        }
    }

    /// <summary>
    /// Shared mouse-to-map-tile math -- SelectMapNodes' original click-only body, extracted so
    /// UpdateHoveredTile (every-frame, not just on click) can reuse the exact same translation
    /// instead of a second, potentially-drifting copy of it.
    /// </summary>
    private bool TryGetHoveredMapPosition(Point mousePosition, out Point mapPosition)
    {
        var relativeMapDisplayMousePosition = new Vector2(mousePosition.X - _contentState.AbsolutePosition.X, mousePosition.Y - _contentState.AbsolutePosition.Y) + _renderPixelOffset;
        var x = (int)(relativeMapDisplayMousePosition.X / _currentTileSize.X);

        if (x < 0 || x >= _tileColumns)
        {
            mapPosition = default;
            return false;
        }

        var y = (int)(relativeMapDisplayMousePosition.Y / _currentTileSize.Y);
        if (y < 0 || y >= _tileRows)
        {
            mapPosition = default;
            return false;
        }

        mapPosition = new Point(x + _currentScrollPosition.X, y + _currentScrollPosition.Y);

        // _tileColumns/_tileRows are the visible viewport grid, which can be larger than
        // the actual map -- a click (or hover) can land within the viewport but past the map's
        // real edge.
        if (!_world.IsOnMap(new Vector3Int(mapPosition.X, mapPosition.Y, 0)))
        {
            mapPosition = default;
            return false;
        }

        return true;
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
            TryConfirmActivation(mousePosition, abilityId);
            return;
        }

        SelectMapNodes(mousePosition);
    }

    /// <summary>
    /// Confirms the armed ability against whichever tile was clicked, provided that tile is
    /// actually within TargetableTiles (clicking outside the highlighted area is a no-op --
    /// the ability stays armed, exactly like clicking empty space doesn't clear an inspector
    /// selection either). Resolves the ability's real Shape anchored on the clicked tile (not
    /// the fixed candidate-enumeration shape ComputeTargetableTiles uses) -- for Adjacent this
    /// produces the same fixed footprint regardless of which of its tiles was clicked, since
    /// Adjacent ignores the cursor entirely.
    /// </summary>
    private void TryConfirmActivation(Point mousePosition, Guid abilityId)
    {
        if (!TryGetHoveredMapPosition(mousePosition, out var clickedColumnRow) ||
            !_abilityCatalog.TryGet(abilityId, out var ability) ||
            !_transformPool.TryGetReadonly(_world.PlayerEntityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var clickedTile = new Vector3Int(clickedColumnRow.X, clickedColumnRow.Y, attackerPosition.Z);

        if (_mapViewState.TargetableTiles is not { } targetableTiles || !targetableTiles.Contains(clickedTile))
        {
            return;
        }

        TargetShapeResolver.Resolve(ability.Targeting.Shape, attackerPosition, clickedTile, ability.Targeting.Range, ability.Targeting.AreaSize, _world.Map.Size, _finalTargetTilesBuffer);
        QueueActivation(_world.PlayerEntityId, abilityId, _finalTargetTilesBuffer);
        Disarm();
    }

    /// <summary>
    /// Cancels an armed ability (right-click tap or Escape -- see OnRightClickTapAction/
    /// OnEscapeAction), or, if nothing is armed, cancels a Delayed ability's in-progress windup
    /// instead: clears PendingDelayedActionComponent and zeroes the shared ActionLock directly
    /// (via ActionLockGate.Lock(..., 0)) so cancelling frees the entity immediately rather than
    /// still waiting out the full wind-up with no effect at the end -- see PendingDelayedActionComponent's
    /// own doc comment.
    /// </summary>
    private void CancelArmedOrPendingAction()
    {
        if (_mapViewState.ArmedAbilityId is not null)
        {
            Disarm();
            return;
        }

        var playerEntityId = _world.PlayerEntityId;
        if (_pendingDelayedActions.Remove(playerEntityId))
        {
            ActionLockGate.Lock(_actionLocks, playerEntityId, framesToWait: 0);
        }
    }

    protected override void OnRightClickTapAction() => CancelArmedOrPendingAction();

    protected override void OnEscapeAction() => CancelArmedOrPendingAction();

    /// <summary>The map's own hotkeys -- only invoked while this window holds focus (see GameInputController.RouteHotkeysToFocusedWindow).</summary>
    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Space))
        {
            IsPaused = !IsPaused;
        }

        HandlePlayerMovementInput(keyboardState);

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Home))
        {
            _cameraFollowsPlayer = true;
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

        HandleAbilityHotkeys(keyboardState, previousKeyboardState);
    }

    /// <summary>
    /// One hotkey slot per HotkeySlotLayout.PhysicalKeyBySlot entry -- an unbound slot's press
    /// is silently a no-op (see HandleHotkeySlotPress), which is exactly what a slot with no
    /// HotkeyBindingComponent instance already produces, so no separate "is this slot enabled"
    /// check is needed here.
    /// </summary>
    private void HandleAbilityHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        foreach (var (slot, physicalKey) in HotkeySlotLayout.PhysicalKeyBySlot)
        {
            if (WasKeyPressed(keyboardState, previousKeyboardState, physicalKey))
            {
                HandleHotkeySlotPress(slot);
            }
        }
    }

    /// <summary>
    /// Arms/disarms the pressed slot's ability, or -- on a double-tap within
    /// DoubleTapWindowFrames -- skips arming entirely and immediately activates against an
    /// auto-picked target (see TryActivateWithAutoTarget). An unbound slot does nothing either
    /// way, per the outline's requirement that unused hotkeys be inert.
    /// </summary>
    private void HandleHotkeySlotPress(HotkeySlot slot)
    {
        var isDoubleTap = _lastHotkeyPressFrameBySlot.TryGetValue(slot, out var lastPressFrame) &&
            _frameCounter - lastPressFrame <= DoubleTapWindowFrames;
        _lastHotkeyPressFrameBySlot[slot] = _frameCounter;

        if (!HotkeyBindingQueries.TryGet(_hotkeyBindings, _world.PlayerEntityId, slot, out var abilityId))
        {
            return;
        }

        if (isDoubleTap)
        {
            TryActivateWithAutoTarget(_world.PlayerEntityId, abilityId);

            // The pair's first press (a moment ago, within the double-tap window) armed this
            // same slot -- now that it's fired, leaving it visually armed would be stale/
            // misleading, so clear it rather than requiring a third press to tidy up.
            if (_mapViewState.ArmedSlot == slot)
            {
                Disarm();
            }

            return;
        }

        if (_mapViewState.ArmedSlot == slot)
        {
            // Pressing the already-armed slot again disarms it. A no-target ability activating
            // on this same press is a later Presentation phase's concern (it needs to know the
            // ability's Shape doesn't require a target tile at all, not just that its slot was
            // pressed again) -- every ability granted so far requires a target, so disarm-only
            // is the complete, correct behavior today.
            Disarm();
            return;
        }

        Arm(slot, abilityId);
    }

    private void Arm(HotkeySlot slot, Guid abilityId)
    {
        _mapViewState.ArmedAbilityId = abilityId;
        _mapViewState.ArmedSlot = slot;

        if (_abilityCatalog.TryGet(abilityId, out var ability) && _transformPool.TryGetReadonly(_world.PlayerEntityId, out var transform))
        {
            ComputeTargetableTiles(transform.Position, ability, _candidateTilesBuffer);
            _mapViewState.TargetableTiles = _candidateTilesBuffer.ToHashSet();
        }
    }

    private void Disarm()
    {
        _mapViewState.ArmedAbilityId = null;
        _mapViewState.ArmedSlot = null;
        _mapViewState.TargetableTiles = null;
    }

    /// <summary>
    /// The full universe of tiles the given ability could possibly be aimed at from
    /// attackerPosition -- Adjacent's fixed self-plus-4-neighbors footprint, or every tile
    /// within the ability's own Range for every cursor-directed shape (SingleTarget/Burst/Line/
    /// Cone) via a Burst-shaped scatter, not the ability's real Shape -- there's no single
    /// "aim direction" yet at arm time, only a reachable area. Shared by Arm (for highlighting)
    /// and TryActivateWithAutoTarget (for double-tap's candidate pool), so the two never drift
    /// out of sync with each other.
    /// </summary>
    private void ComputeTargetableTiles(Vector3Int attackerPosition, AbilityDefinition ability, List<Vector3Int> buffer)
    {
        if (ability.Targeting.Shape == TargetShape.Adjacent)
        {
            TargetShapeResolver.Resolve(TargetShape.Adjacent, attackerPosition, attackerPosition, range: 0, areaSize: 0, _world.Map.Size, buffer);
            return;
        }

        TargetShapeResolver.Resolve(TargetShape.Burst, attackerPosition, attackerPosition, range: 0, ability.Targeting.Range, _world.Map.Size, buffer);
    }

    /// <summary>
    /// Resolves and queues a full activation with no manual click-confirm at all -- the
    /// double-tap path. Adjacent's footprint never depends on a target choice (it's always the
    /// caster's own tile plus its 4 cardinal neighbors), so it's queued immediately. Every other
    /// shape needs a target tile chosen first: ComputeTargetableTiles' reachable-area candidates
    /// are filtered down to occupied tiles and handed to TargetPriority.SelectAutoTarget, using
    /// MapViewState.HoveredTile as the cursor bias when one is already tracked (armed-and-then-
    /// double-tapped in one motion means Update hasn't run with the arm in effect yet, so
    /// HoveredTile can still be stale/null on the very first pair -- attackerPosition is the
    /// fallback for exactly that case, which is also what makes "closest to cursor" degenerate
    /// harmlessly into "closest to the caster" rather than picking an arbitrary target).
    /// </summary>
    private void TryActivateWithAutoTarget(int entityId, Guid abilityId)
    {
        if (!_abilityCatalog.TryGet(abilityId, out var ability) || !_transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var mapSize = _world.Map.Size;

        if (ability.Targeting.Shape == TargetShape.Adjacent)
        {
            ComputeTargetableTiles(attackerPosition, ability, _candidateTilesBuffer);
            QueueActivation(entityId, abilityId, _candidateTilesBuffer);
            return;
        }

        ComputeTargetableTiles(attackerPosition, ability, _candidateTilesBuffer);

        _occupiedCandidateTilesBuffer.Clear();
        foreach (var tile in _candidateTilesBuffer)
        {
            var occupantEntityId = _world.GetEntityIdAt(tile);
            if (occupantEntityId != -1 && occupantEntityId != entityId)
            {
                _occupiedCandidateTilesBuffer.Add(tile);
            }
        }

        var cursorTile = _mapViewState.HoveredTile ?? attackerPosition;
        if (TargetPriority.SelectAutoTarget(attackerPosition, cursorTile, _occupiedCandidateTilesBuffer) is not { } chosenTile)
        {
            return;
        }

        TargetShapeResolver.Resolve(ability.Targeting.Shape, attackerPosition, chosenTile, ability.Targeting.Range, ability.Targeting.AreaSize, mapSize, _finalTargetTilesBuffer);
        QueueActivation(entityId, abilityId, _finalTargetTilesBuffer);
    }

    /// <summary>Presentation only ever queues an activation request -- AbilityActivationSystem is the only thing that applies gameplay effects. Mirrors TryQueuePlayerMove's existing queue-and-let-a-system-consume pattern for movement.</summary>
    private void QueueActivation(int entityId, Guid abilityId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        _pendingActivations.Merge(entityId, new PendingAbilityActivationComponent(abilityId, targetTiles.ToArray()));
    }

    private void CycleZoom(int direction)
    {
        var zoomLevels = Enum.GetValues<ZoomLevel>();
        var currentIndex = Array.IndexOf(zoomLevels, _currentZoomLevel);
        var newIndex = MathUtility.ClampInt(currentIndex + direction, 0, zoomLevels.Length - 1);
        UpdateZoomLevel(zoomLevels[newIndex]);
    }

    private void HandlePlayerMovementInput(KeyboardState keyboardState)
    {
        if (_playerMoveCooldownFrames > 0)
        {
            _playerMoveCooldownFrames--;
        }

        var delta = new Vector3Int();
        if (keyboardState.IsKeyDown(Keys.W))
        {
            delta.Y -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.S))
        {
            delta.Y += 1;
        }
        if (keyboardState.IsKeyDown(Keys.A))
        {
            delta.X -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.D))
        {
            delta.X += 1;
        }

        if (delta == new Vector3Int() || _playerMoveCooldownFrames > 0)
        {
            return;
        }

        _playerMoveCooldownFrames = FramesPerPlayerMove;
        TryQueuePlayerMove(delta);
    }

    private void TryQueuePlayerMove(Vector3Int delta)
    {
        var playerEntityId = _world.PlayerEntityId;
        if (!_transformPool.TryGetReadonly(playerEntityId, out var transformComponent) ||
            !_movementPool.TryGetReadonly(playerEntityId, out var movementComponent))
        {
            return;
        }

        // Only queue a new move while at rest -- avoids redirecting a move that's already
        // pending (e.g. still waiting on MovementSystem's action lock).
        var isAtRest = movementComponent.NextMapPosition is null || movementComponent.NextMapPosition.Value == transformComponent.Position;
        if (!isAtRest)
        {
            return;
        }

        var candidate = transformComponent.Position + delta;
        var occupyingEntityId = _world.GetEntityIdAt(candidate);
        if (!_world.IsOnMap(candidate) || (occupyingEntityId != -1 && occupyingEntityId != playerEntityId))
        {
            return;
        }

        _movementPool.TryUpdate(playerEntityId, candidate, static (ref MovementComponent movement, Vector3Int target) =>
        {
            movement.NextMapPosition = target;
        });
    }

    private void CenterCameraOn(Vector3Int position)
    {
        var desiredScroll = new Point(position.X - _tileColumns / 2, position.Y - _tileRows / 2);
        _currentScrollPosition = new Point(
            MathUtility.ClampInt(desiredScroll.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(desiredScroll.Y, 0, _maxScrollPosition.Y));

        _renderPixelOffset = Vector2.Zero;

        ResetBackgroundColorCache();
    }

    /// <summary>Snapshots the scroll position the moment a right-mouse-drag starts, so OnRightDragAction always has a fixed anchor to measure the drag against.</summary>
    protected override void OnRightDragStartAction()
    {
        _rightDragStartScrollPosition = _currentScrollPosition;
    }

    protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart)
    {
        if (totalPixelDeltaSinceStart == Vector2.Zero)
        {
            return;
        }

        _cameraFollowsPlayer = false;

        var continuousPixelPosition = new Vector2(
             MathHelper.Clamp(_rightDragStartScrollPosition.X * _currentTileSize.X - totalPixelDeltaSinceStart.X, 0, _maxScrollPosition.X * _currentTileSize.X),
             MathHelper.Clamp(_rightDragStartScrollPosition.Y * _currentTileSize.Y - totalPixelDeltaSinceStart.Y, 0, _maxScrollPosition.Y * _currentTileSize.Y));

        var wholeTileScroll = new Point(
            (int)(continuousPixelPosition.X / _currentTileSize.X),
            (int)(continuousPixelPosition.Y / _currentTileSize.Y));

        _renderPixelOffset = new Vector2(
            continuousPixelPosition.X - wholeTileScroll.X * _currentTileSize.X,
            continuousPixelPosition.Y - wholeTileScroll.Y * _currentTileSize.Y);

        var scrollChange = new Point(wholeTileScroll.X - _currentScrollPosition.X, wholeTileScroll.Y - _currentScrollPosition.Y);
        if (scrollChange != Point.Zero)
        {
            UpdateScrollPosition(scrollChange);
        }
    }

    protected override void OnRightDragEndAction()
    {
        if (_renderPixelOffset == Vector2.Zero)
        {
            return;
        }

        var snap = new Point(
            _renderPixelOffset.X >= _currentTileSize.X / 2f ? 1 : 0,
            _renderPixelOffset.Y >= _currentTileSize.Y / 2f ? 1 : 0);

        _renderPixelOffset = Vector2.Zero;

        if (snap != Point.Zero)
        {
            UpdateScrollPosition(snap);
        }
    }
}