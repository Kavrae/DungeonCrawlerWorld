using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using FontStashSharp;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// Displays a scrollable/zoomable viewport onto a single MapLayer of the game map at a time
/// (Page Up/Down switches layers -- see GameInputController.ChangeLayer). Cannot be moved,
/// docked, or resized -- no chrome behaviors are attached to it. Holds direct
/// constructor-injected references to World/MapViewState/ComponentManager rather than a pluggable content
/// abstraction (unlike DebugWindowContent/SelectionWindowContent/NotificationCenter, which
/// implement IWindowContent), since the map's rendering is tightly coupled to
/// World/ComponentManager and gains nothing from the extra indirection.
/// </summary>
public sealed class MapWindow : Window
{
    private const int MaxTinyEntitiesDrawn = 9;
    private const int TinyGridDimension = 3;
    private static readonly Color UpLayerBadgeColor = Color.Blue;
    private static readonly Color DownLayerBadgeColor = new(101, 67, 33);

    private readonly World _world;
    private readonly MapViewState _mapViewState;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly DirectComponentPool<BackgroundComponent> _backgroundPool;
    private readonly PackedComponentPool<OccupancyComponent> _occupancyPool;
    private readonly TileRenderer _tileRenderer;
    private readonly GlyphRenderer _glyphRenderer;

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

    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;
    private static readonly Dictionary<ZoomLevel, Point> TileSizesByZoomLevel = new()
    {
        [ZoomLevel.Team] = new Point(12, 12),
        [ZoomLevel.Neighborhood] = new Point(6, 6),
        [ZoomLevel.Borough] = new Point(3, 3),
    };

    private Color[] _backgroundColorCache = [];

    public MapWindow(
        FontService fontService,
        WindowService windowService,
        World world,
        MapViewState mapViewState,
        ComponentManager componentManager,
        TileRenderer tileRenderer,
        GlyphRenderer glyphRenderer) : base(fontService, windowService)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapViewState);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(tileRenderer);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        _world = world;
        _mapViewState = mapViewState;
        _transformPool = componentManager.GetDirectPool<TransformComponent>();
        _glyphPool = componentManager.GetDirectPool<GlyphComponent>();
        _backgroundPool = componentManager.GetDirectPool<BackgroundComponent>();
        _occupancyPool = componentManager.GetPackedPool<OccupancyComponent>();
        _tileRenderer = tileRenderer;
        _glyphRenderer = glyphRenderer;

        _tileDepth = _world.Map.Size.Z;
    }

    public override void Initialize()
    {
        base.Initialize();

        _mediumFont = FontService.GetFont(8);
        _largeFont = FontService.GetFont(24);
        _hugeFont = FontService.GetFont(36);
        _tinyFont = FontService.GetFont(3); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = FontService.GetFont(6); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        // UpdateMaxScrollPosition reads _tileColumns/_tileRows, which UpdateTileSizes is what
        // actually computes -- must run second, not first, or max scroll is calculated
        // against a still-zero visible tile count (letting the map scroll a full extra
        // viewport past its real edge).
        UpdateTileSizes();
        UpdateMaxScrollPosition();

        // MapViewState.CurrentMapLayer defaults to MapLayer.Ground (index 1) regardless of how deep
        // this particular Map actually is -- fine for the real 3-layer game map, but a
        // shallower Map (e.g. a 1-deep test map) would leave it out of bounds for
        // Map.GetEntityId/GetTerrainEntityId below. Clamp the same way ChangeLayer does before
        // anything reads it.
        _mapViewState.CurrentMapLayer = MathUtility.ClampInt(_mapViewState.CurrentMapLayer, 0, _tileDepth - 1);

        ResetBackgroundColorCache();
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        _tileRenderer.DrawBackgrounds(spriteBatch, unitRectangle, _backgroundColorCache, _tileColumns, _tileRows, _currentTileSize);
        DrawSelectedTileHighlight(spriteBatch, unitRectangle);
        DrawGlyphs(spriteBatch);
    }

    private void DrawSelectedTileHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        var column = selectedPosition.X - _currentScrollPosition.X;
        var row = selectedPosition.Y - _currentScrollPosition.Y;

        if (column < 0 || column >= _tileColumns || row < 0 || row >= _tileRows)
        {
            return;
        }

        var outerRectangle = new Rectangle(column * _currentTileSize.X, row * _currentTileSize.Y, _currentTileSize.X, _currentTileSize.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, Color.Gold);

        var innerRectangle = new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, _innerTileSize.X, _innerTileSize.Y);
        spriteBatch.Draw(unitRectangle, innerRectangle, _backgroundColorCache[column + row * _tileColumns]);
    }

    /// <summary>
    /// Switches the single MapLayer this window renders (Page Up/Down -- see
    /// GameInputController), stored on MapViewState rather than locally so SelectionWindowContent
    /// can scope the inspector to the same layer this window is actually showing. Background
    /// depends on the current layer's terrain (see ResolveBackgroundColor), so the cache must
    /// be rebuilt on every change, the same as a zoom-level change.
    /// </summary>
    public void ChangeLayer(int delta)
    {
        _mapViewState.CurrentMapLayer = MathUtility.ClampInt(_mapViewState.CurrentMapLayer + delta, 0, _tileDepth - 1);
        ResetBackgroundColorCache();
    }

    private void DrawGlyphs(SpriteBatch spriteBatch)
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

                var tileOrigin = new Vector2(columnIndex * _currentTileSize.X, rowIndex * _currentTileSize.Y);
                occupantsByPosition.TryGetValue(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer), out var occupantsHere);

                // Draw order (bottom to top): terrain floor glyph, tiny entities in their 3x3
                // sub-grid, the main Blocking occupant on top of them, Phasing entities
                // translucent over everything, then the small layer-occupancy badges.
                DrawTerrainGlyph(spriteBatch, terrainLayer, mapNodeX, mapNodeY, tileOrigin);
                DrawTinyGrid(spriteBatch, occupantsHere, tileOrigin);
                DrawMainGlyph(spriteBatch, currentMapLayer, mapNodeX, mapNodeY, columnIndex, rowIndex);
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
            if (!_transformPool.Has(entityId))
            {
                continue;
            }

            var position = _transformPool.GetReadonly(entityId).Position;
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
        if (terrainEntityId == -1 || !_glyphPool.Has(terrainEntityId))
        {
            return;
        }

        ref readonly var glyphComponent = ref _glyphPool.GetReadonly(terrainEntityId);
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

            if (!_occupancyPool.GetReadonly(entityId).IsTiny || !_glyphPool.Has(entityId))
            {
                continue;
            }

            var subColumn = drawnCount % TinyGridDimension;
            var subRow = drawnCount / TinyGridDimension;
            ref readonly var glyphComponent = ref _glyphPool.GetReadonly(entityId);
            var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

            _glyphRenderer.DrawCentered(spriteBatch, _tinyFont, glyphComponent.Glyph, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y), glyphComponent.GlyphColor);
            drawnCount++;
        }
    }

    /// <summary>The Blocking occupant of the current layer's Map slot -- only ever one, and only ever Blocking (see World.IsBlocking).</summary>
    private void DrawMainGlyph(SpriteBatch spriteBatch, int currentMapLayer, int mapNodeX, int mapNodeY, int columnIndex, int rowIndex)
    {
        var entityId = _world.Map.GetEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
        if (entityId == -1)
        {
            return;
        }

        if (!_glyphPool.Has(entityId) || !_transformPool.Has(entityId))
        {
            return;
        }

        ref readonly var glyphComponent = ref _glyphPool.GetReadonly(entityId);
        ref readonly var transformComponent = ref _transformPool.GetReadonly(entityId);

        // Multi-tile glyph fix: only draw from the entity's top-left origin tile
        // to avoid drawing it once per occupied tile.
        if (transformComponent.Position.X != mapNodeX || transformComponent.Position.Y != mapNodeY)
        {
            return;
        }

        // The footprint is Size tiles wide/tall, not 1 -- a 3x3 Huge entity's glyph must
        // center across all three tiles it actually occupies, not just the origin tile.
        var footprintTopLeft = new Vector2(columnIndex * _currentTileSize.X, rowIndex * _currentTileSize.Y);
        var footprintSize = new Vector2(transformComponent.Size.X * _currentTileSize.X, transformComponent.Size.Y * _currentTileSize.Y);

        _glyphRenderer.DrawCentered(spriteBatch, FontForSize(transformComponent.Size.X), glyphComponent.Glyph, footprintTopLeft, footprintSize, glyphComponent.GlyphColor);
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
            if (!_occupancyPool.GetReadonly(entityId).IsPhasing || !_glyphPool.Has(entityId) || !_transformPool.Has(entityId))
            {
                continue;
            }

            ref readonly var glyphComponent = ref _glyphPool.GetReadonly(entityId);
            ref readonly var transformComponent = ref _transformPool.GetReadonly(entityId);
            var footprintSize = new Vector2(transformComponent.Size.X * _currentTileSize.X, transformComponent.Size.Y * _currentTileSize.Y);

            _glyphRenderer.DrawCentered(spriteBatch, FontForSize(transformComponent.Size.X), glyphComponent.Glyph, tileOrigin, footprintSize, glyphComponent.GlyphColor * 0.5f);
        }
    }

    /// <summary>Blue up-arrow (top-right) if any layer above the current one is occupied; brown down-arrow (bottom-right) if any layer below is.</summary>
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

        // +1 to account for partial tiles when scrolling.
        _tileColumns = (int)System.Math.Floor(ContentSize.X / _currentTileSize.X) + 1;
        _tileRows = (int)System.Math.Floor(ContentSize.Y / _currentTileSize.Y) + 1;

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
        if (occupantEntityId != -1 && _backgroundPool.Has(occupantEntityId))
        {
            return _backgroundPool.GetReadonly(occupantEntityId).BackgroundColor;
        }

        if (Map.TerrainLayerFor(currentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = _world.Map.GetTerrainEntityId(mapNodeX, mapNodeY, terrainLayer);
            if (terrainEntityId != -1 && _backgroundPool.Has(terrainEntityId))
            {
                return _backgroundPool.GetReadonly(terrainEntityId).BackgroundColor;
            }
        }

        return Color.White;
    }

    public void SelectMapNodes(Point mousePosition)
    {
        var relativeMapDisplayMousePosition = new Vector2(mousePosition.X - _contentAbsolutePosition.X, mousePosition.Y - _contentAbsolutePosition.Y);
        var x = (int)(relativeMapDisplayMousePosition.X / _currentTileSize.X);

        if (x < 0 || x >= _tileColumns)
        {
            return;
        }

        var y = (int)(relativeMapDisplayMousePosition.Y / _currentTileSize.Y);
        if (y < 0 || y >= _tileRows)
        {
            return;
        }

        var mapPosition = new Point(x + _currentScrollPosition.X, y + _currentScrollPosition.Y);

        // _tileColumns/_tileRows are the visible viewport grid, which can be larger than
        // the actual map -- a click can land within the viewport but past the map's real edge.
        if (!_world.IsOnMap(new Vector3Int(mapPosition.X, mapPosition.Y, 0)))
        {
            return;
        }

        _mapViewState.SelectedMapNodePosition = mapPosition;
    }

    protected override void OnContentClickAction(Point mousePosition) => SelectMapNodes(mousePosition);
}
