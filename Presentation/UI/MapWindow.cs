using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using FontStashSharp;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
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
    private readonly MapTintGrid _tintGrid;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly DirectComponentPool<SpriteComponent> _spritePool;
    private readonly MultiComponentPool<NonBlockingComponent> _nonBlockingPool;
    private readonly PackedComponentPool<HealthComponent> _healthPool;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _deadPool;
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions;
    private readonly TileRenderer _tileRenderer;
    private readonly GlyphRenderer _glyphRenderer;
    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

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
        ElementPoolService elementPoolService,
        World world,
        MapViewState mapViewState,
        ComponentManager componentManager,
        AbilityCatalog abilityCatalog,
        ItemCatalog itemCatalog,
        TileRenderer tileRenderer,
        GlyphRenderer glyphRenderer,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer) : base(fontService, elementPoolService, glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapViewState);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(abilityCatalog);
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(tileRenderer);
        ArgumentNullException.ThrowIfNull(glyphRenderer);
        ArgumentNullException.ThrowIfNull(spriteSheetService);
        ArgumentNullException.ThrowIfNull(spriteRenderer);

        _world = world;
        _mapViewState = mapViewState;
        _transformPool = componentManager.GetDirectPool<TransformComponent>();
        _glyphPool = componentManager.GetDirectPool<GlyphComponent>();
        _spritePool = componentManager.GetDirectPool<SpriteComponent>();
        _nonBlockingPool = componentManager.GetMultiPool<NonBlockingComponent>();
        _healthPool = componentManager.GetPackedPool<HealthComponent>();
        _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        _deadPool = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        _pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();
        _tileRenderer = tileRenderer;
        _glyphRenderer = glyphRenderer;
        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;

        _camera = new MapCamera(world);
        _abilityTargeting = new AbilityTargetingController(
            world,
            mapViewState,
            _camera,
            abilityCatalog,
            itemCatalog,
            _transformPool,
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetMultiPool<ActionHotkeyBindingComponent>(),
            componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetPackedPool<PendingAbilityActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>());
        _tintGrid = new MapTintGrid(componentManager, world.Map.Size);
        _backgroundCache = new MapBackgroundCache(
            world,
            mapViewState,
            componentManager.GetDirectPool<BackgroundComponent>(),
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
        DrawGlyphs(spriteBatch, unitRectangle);
        DrawGlowOverlay(spriteBatch, unitRectangle);
        DrawTargetingHighlights(spriteBatch, unitRectangle);
        DrawSelectedTileHighlight(spriteBatch, unitRectangle);
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
    private void DrawGlowOverlay(SpriteBatch spriteBatch, Texture2D unitRectangle)
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

                var tileOrigin = TileOrigin(columnIndex, rowIndex);
                var destination = new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
                spriteBatch.Draw(unitRectangle, destination, tint.Color * tint.Factor);
            }
        }
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
    ///
    /// Once a Delayed ability is actually queued, Disarm already clears TargetableTiles (there's
    /// nothing left to aim), but the player benefits from still seeing exactly which tiles are
    /// about to be hit once the windup ends -- so this falls back to highlighting the player's
    /// PendingDelayedActionComponent.TargetTiles (the already-resolved, locked-in footprint) in
    /// the same red used for a confirmed hover target, for as long as that pending action exists.
    /// </summary>
    private void DrawTargetingHighlights(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.TargetableTiles is { Count: > 0 } targetableTiles)
        {
            foreach (var tile in targetableTiles)
            {
                var borderColor = _abilityTargeting.HoveredFootprintContains(tile) ? HoveredTargetTileBorderColor : TargetableTileBorderColor;
                DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor);
            }
            return;
        }

        if (_pendingDelayedActions.TryGetReadonly(_world.PlayerEntityId, out var pending))
        {
            foreach (var tile in pending.TargetTiles)
            {
                DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, HoveredTargetTileBorderColor);
            }
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
        var column = mapNodeX - _camera.CurrentScrollPosition.X;
        var row = mapNodeY - _camera.CurrentScrollPosition.Y;

        if (column < 0 || column >= _camera.TileColumns || row < 0 || row >= _camera.TileRows)
        {
            return;
        }

        var origin = TileOrigin(column, row);
        var tileRectangle = new Rectangle((int)origin.X, (int)origin.Y, _camera.CurrentTileSize.X, _camera.CurrentTileSize.Y);
        spriteBatch.Draw(unitRectangle, tileRectangle, borderColor * TargetSelectionMaskAlpha);
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

    /// <summary>
    /// Two full passes over the visible grid, not one interleaved pass -- a multi-tile
    /// entity's sprite/glyph is drawn once, from its origin tile (see DrawPrimaryOccupant),
    /// covering every tile in its footprint. With a single per-tile pass, a neighboring
    /// column/row's terrain draw (a later loop iteration, since SpriteSortMode.Deferred
    /// submits in call order with no depth buffer) would land on top of that already-drawn
    /// footprint, covering part of it -- visible now that terrain renders as an opaque
    /// full-tile sprite rather than a small, mostly-transparent glyph. Drawing all terrain
    /// first, then all occupants, guarantees occupants are always on top regardless of
    /// footprint size or the entity's position within it.
    /// </summary>
    private void DrawGlyphs(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var currentMapLayer = _mapViewState.CurrentMapLayer;
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

                DrawTerrainGlyph(spriteBatch, terrainLayer, mapNodeX, mapNodeY, TileOrigin(columnIndex, rowIndex));
            }
        }

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
                var occupantsHere = _world.GetNonBlockingEntityIdsAt(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));

                DrawCorpses(spriteBatch, occupantsHere, mapNodeX, mapNodeY, tileOrigin);
                DrawTinyGrid(spriteBatch, occupantsHere, tileOrigin);
                DrawPrimaryOccupant(spriteBatch, unitRectangle, currentMapLayer, mapNodeX, mapNodeY, columnIndex, rowIndex);
                DrawPhasingGlyphs(spriteBatch, occupantsHere, tileOrigin);
                DrawLayerBadges(spriteBatch, currentMapLayer, mapNodeX, mapNodeY, tileOrigin);
            }
        }
    }

    /// <summary>Draws entityId's sprite if it has one, else falls back to its glyph -- the one place that decides sprite-vs-glyph, shared by every per-tile visual draw below. Returns whether anything was actually drawn. A corpse (DeadComponent) draws with a flat Color.Gray tint instead of its normal color -- a color-multiply override, not a true desaturation shader (no shader/Effect infrastructure exists here). Delegates the actual draw to SpriteOrGlyphRenderer, shared with Folder/inventory item cells -- this method's only job is resolving entityId's own sprite/glyph/dead-tint inputs.</summary>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, float alphaMultiplier = 1f)
    {
        var isDead = _deadPool?.Has(entityId) == true;

        SpriteComponent? sprite = _spritePool.TryGetReadonly(entityId, out var spriteComponent) ? spriteComponent : null;
        var glyph = _glyphPool.TryGetReadonly(entityId, out var glyphComponent) ? glyphComponent.Glyph : string.Empty;
        var glyphColor = isDead ? Color.Gray : glyphComponent.GlyphColor;
        var spriteTint = isDead ? Color.Gray : Color.White;

        return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, _glyphRenderer, sprite, font, glyph, glyphColor, footprintTopLeft, footprintSize, spriteTint, alphaMultiplier);
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

    /// <summary>Up to 9 Tiny entities in a 3x3 sub-grid, each &lt;= 1/3 tile size; extras beyond 9 are simply not drawn.</summary>
    private void DrawTinyGrid(SpriteBatch spriteBatch, IReadOnlyList<int> occupants, Vector2 tileOrigin)
    {
        var subCellSize = new Point(_camera.CurrentTileSize.X / TinyGridDimension, _camera.CurrentTileSize.Y / TinyGridDimension);
        var drawnCount = 0;

        foreach (var entityId in occupants)
        {
            if (drawnCount >= MaxTinyEntitiesDrawn)
            {
                break;
            }

            if ((NonBlockingQueries.CombinedKind(_nonBlockingPool, entityId) & NonBlockingKind.Tiny) == 0)
            {
                continue;
            }

            var subColumn = drawnCount % TinyGridDimension;
            var subRow = drawnCount / TinyGridDimension;
            var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

            if (TryDrawEntityVisual(spriteBatch, entityId, _tinyFont, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y)))
            {
                drawnCount++;
            }
        }
    }

    private void DrawPrimaryOccupant(SpriteBatch spriteBatch, Texture2D unitRectangle, int currentMapLayer, int mapNodeX, int mapNodeY, int columnIndex, int rowIndex)
    {
        var entityId = _world.Map.GetEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
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
    }

    /// <summary>
    /// Draws each corpse (DeadComponent-marked occupant in the non-Blocking index) at its own
    /// full footprint -- the same treatment DrawPrimaryOccupant gives the single Blocking
    /// occupant, just sourced from the non-Blocking list instead: a corpse that used to be
    /// Blocking no longer holds that slot (see DeathSystem/World.ConvertToNonBlocking), and
    /// TryDrawEntityVisual's own DeadComponent check is what actually grey-tints it. A corpse
    /// that was already non-Blocking when it died (e.g. a Phasing Ghost) is drawn by whichever
    /// existing path already handles its Kind (DrawTinyGrid/DrawPhasingGlyphs), not here --
    /// this only covers the "no NonBlockingKind flag" case those two paths don't draw at all.
    /// </summary>
    private void DrawCorpses(SpriteBatch spriteBatch, IReadOnlyList<int> occupants, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        if (_deadPool is null)
        {
            return;
        }

        foreach (var entityId in occupants)
        {
            if (!_deadPool.Has(entityId) || (NonBlockingQueries.CombinedKind(_nonBlockingPool, entityId) & (NonBlockingKind.Tiny | NonBlockingKind.Phasing)) != 0)
            {
                continue;
            }

            if (!_transformPool.TryGetReadonly(entityId, out var transformComponent) ||
                transformComponent.Position.X != mapNodeX || transformComponent.Position.Y != mapNodeY)
            {
                continue;
            }

            var footprintSize = new Vector2(transformComponent.Size.X * _camera.CurrentTileSize.X, transformComponent.Size.Y * _camera.CurrentTileSize.Y);
            TryDrawEntityVisual(spriteBatch, entityId, FontForSize(transformComponent.Size.X), tileOrigin, footprintSize);
        }
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
        if (!_healthPool.TryGetReadonly(entityId, out var health) || health.MaximumHealth <= 0)
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
        if (effectiveMaximumHealth <= 0 || health.CurrentHealth >= effectiveMaximumHealth)
        {
            return;
        }

        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = footprintTopLeft.Y;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var healthFraction = health.CurrentHealth / effectiveMaximumHealth;
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
    private void DrawPhasingGlyphs(SpriteBatch spriteBatch, IReadOnlyList<int> occupants, Vector2 tileOrigin)
    {
        foreach (var entityId in occupants)
        {
            if ((NonBlockingQueries.CombinedKind(_nonBlockingPool, entityId) & NonBlockingKind.Phasing) == 0 ||
                !_transformPool.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            var footprintSize = new Vector2(transformComponent.Size.X * _camera.CurrentTileSize.X, transformComponent.Size.Y * _camera.CurrentTileSize.Y);

            TryDrawEntityVisual(spriteBatch, entityId, FontForSize(transformComponent.Size.X), tileOrigin, footprintSize, alphaMultiplier: 0.5f);
        }
    }

    /// <summary>
    /// Blue up-arrow (top-right) if any layer above the current one is occupied; brown
    /// down-arrow (bottom-right) if any layer below is. A tile-level badge -- unlike
    /// DrawEntityIcons, this describes the tile's other layers, not the Blocking occupant
    /// drawn on it.
    /// </summary>
    private void DrawLayerBadges(SpriteBatch spriteBatch, int currentMapLayer, int mapNodeX, int mapNodeY, Vector2 tileOrigin)
    {
        var hasHigherLayer = false;
        for (var layer = currentMapLayer + 1; layer < _tileDepth; layer++)
        {
            if (IsLayerOccupied(mapNodeX, mapNodeY, layer))
            {
                hasHigherLayer = true;
                break;
            }
        }

        var hasLowerLayer = false;
        for (var layer = currentMapLayer - 1; layer >= 0; layer--)
        {
            if (IsLayerOccupied(mapNodeX, mapNodeY, layer))
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

    /// <summary>"Occupied" counts a Blocking entity in Map's slot exactly the same as a non-Blocking entity found via the position-keyed index.</summary>
    private bool IsLayerOccupied(int mapNodeX, int mapNodeY, int layer)
    {
        var position = new Vector3Int(mapNodeX, mapNodeY, layer);
        return _world.Map.GetEntityId(position) != -1 || _world.GetNonBlockingEntityIdsAt(position).Count > 0;
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
    /// A left-click confirms the armed ability/item's activation if either is armed, falling
    /// back to the ordinary inspector click-select otherwise -- an armed ability or item's target
    /// selection takes over the click entirely while it's active, matching how the outline
    /// describes left-click as the universal "activate" gesture once something is armed.
    /// </summary>
    protected override void OnContentClickAction(Point mousePosition)
    {
        if (_mapViewState.ArmedAbilityId is not null || _mapViewState.ArmedItemDefinitionId is not null)
        {
            _abilityTargeting.TryConfirmActivation(mousePosition, _contentState.AbsolutePosition);
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