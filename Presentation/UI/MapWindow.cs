using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// Displays a scrollable/zoomable viewport onto the game map. Cannot be moved, docked, or
/// resized -- no chrome behaviors are attached to it. Holds direct constructor-injected
/// references to World/ComponentManager rather than a pluggable content abstraction (unlike
/// DebugWindowContent/SelectionWindowContent/NotificationCenter, which implement
/// IWindowContent), since the map's rendering is tightly coupled to World/ComponentManager
/// and gains nothing from the extra indirection.
/// </summary>
public sealed class MapWindow : Window
{
    private readonly World _world;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly DirectComponentPool<GlyphComponent> _glyphPool;
    private readonly DirectComponentPool<BackgroundComponent> _backgroundPool;
    private readonly TileRenderer _tileRenderer;
    private readonly GlyphRenderer _glyphRenderer;

    private SpriteFontBase _mediumFont = null!;
    private SpriteFontBase _largeFont = null!;
    private SpriteFontBase _hugeFont = null!;

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
        ComponentManager componentManager,
        TileRenderer tileRenderer,
        GlyphRenderer glyphRenderer) : base(fontService, windowService)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(tileRenderer);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        _world = world;
        _transformPool = componentManager.GetDirectPool<TransformComponent>();
        _glyphPool = componentManager.GetDirectPool<GlyphComponent>();
        _backgroundPool = componentManager.GetDirectPool<BackgroundComponent>();
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

        // UpdateMaxScrollPosition reads _tileColumns/_tileRows, which UpdateTileSizes is what
        // actually computes -- must run second, not first, or max scroll is calculated
        // against a still-zero visible tile count (letting the map scroll a full extra
        // viewport past its real edge).
        UpdateTileSizes();
        UpdateMaxScrollPosition();
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
        if (_world.SelectedMapNodePosition is not { } selectedPosition)
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

    private void DrawGlyphs(SpriteBatch spriteBatch)
    {
        for (var columnIndex = 0; columnIndex < _tileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < _tileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + _currentScrollPosition.X;
                var mapNodeY = rowIndex + _currentScrollPosition.Y;

                if (!_world.IsOnMap(new Engine.Math.Vector3Int(mapNodeX, mapNodeY, 0)))
                {
                    continue;
                }

                for (var heightIndex = _tileDepth - 1; heightIndex >= 0; heightIndex--)
                {
                    var mapNode = _world.Map.MapNodes[mapNodeX, mapNodeY, heightIndex];
                    if (mapNode.EntityId == -1)
                    {
                        continue;
                    }

                    var entityId = mapNode.EntityId;
                    if (!_glyphPool.Has(entityId) || !_transformPool.Has(entityId))
                    {
                        continue;
                    }

                    ref readonly var glyphComponent = ref _glyphPool.GetReadonly(entityId);
                    ref readonly var transformComponent = ref _transformPool.GetReadonly(entityId);

                    // Multi-tile glyph fix: only draw from the entity's top-left origin tile
                    // to avoid drawing it once per occupied tile.
                    if (transformComponent.Position.X != mapNodeX || transformComponent.Position.Y != mapNodeY)
                    {
                        break;
                    }

                    var glyphFont = transformComponent.Size.X switch
                    {
                        1 => _mediumFont,
                        2 => _largeFont,
                        _ => _hugeFont,
                    };

                    var drawPosition = new Vector2(
                        columnIndex * _currentTileSize.X + glyphComponent.GlyphOffset.X,
                        rowIndex * _currentTileSize.Y + glyphComponent.GlyphOffset.Y);

                    _glyphRenderer.Draw(spriteBatch, glyphFont, glyphComponent.Glyph, drawPosition, glyphComponent.GlyphColor);
                    break;
                }
            }
        }
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
            Engine.Math.MathUtility.ClampInt(_currentScrollPosition.X, 0, _maxScrollPosition.X),
            Engine.Math.MathUtility.ClampInt(_currentScrollPosition.Y, 0, _maxScrollPosition.Y));

        ResetBackgroundColorCache();
    }

    public void UpdateScrollPosition(Point scrollChange)
    {
        var previousScrollPosition = _currentScrollPosition;

        _currentScrollPosition = new Point(
            Engine.Math.MathUtility.ClampInt(_currentScrollPosition.X + scrollChange.X, 0, _maxScrollPosition.X),
            Engine.Math.MathUtility.ClampInt(_currentScrollPosition.Y + scrollChange.Y, 0, _maxScrollPosition.Y));

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
                _backgroundColorCache[column + row * _tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
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
            _backgroundColorCache[column + row * _tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
        }
    }

    private void FillBackgroundRow(int row)
    {
        var mapNodeY = row + _currentScrollPosition.Y;
        for (var column = 0; column < _tileColumns; column++)
        {
            var mapNodeX = column + _currentScrollPosition.X;
            _backgroundColorCache[column + row * _tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
        }
    }

    private Color ResolveTopBackgroundColor(int mapNodeX, int mapNodeY)
    {
        if (!_world.IsOnMap(new Engine.Math.Vector3Int(mapNodeX, mapNodeY, 0)))
        {
            return Color.Black;
        }

        for (var z = _tileDepth - 1; z >= 0; z--)
        {
            var mapNode = _world.Map.MapNodes[mapNodeX, mapNodeY, z];
            if (mapNode.EntityId != -1 && _backgroundPool.Has(mapNode.EntityId))
            {
                return _backgroundPool.GetReadonly(mapNode.EntityId).BackgroundColor;
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
        if (!_world.IsOnMap(new Engine.Math.Vector3Int(mapPosition.X, mapPosition.Y, 0)))
        {
            return;
        }

        _world.SelectedMapNodePosition = mapPosition;
    }

    protected override void OnContentClickAction(Point mousePosition) => SelectMapNodes(mousePosition);
}
