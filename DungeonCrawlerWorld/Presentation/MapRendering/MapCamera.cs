using Engine.Math;
using Engine.Tiles;
using Game.Views;
using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>
/// A map viewport: which tiles are visible, at what size, scrolled and dragged where, and the screen to tile math over them.
/// </summary>
/// <remarks>
/// Reads the map only through IMapViewQuery (its bounds and which tiles are on it), so it serves a live session and an
/// authored layout alike. Every method that repositions the camera (UpdateScrollPosition/ApplyDrag/EndDrag) returns
/// the actually-applied delta rather than reaching out to invalidate anything itself -- the terrain caches these
/// deltas drive are MapRenderer's concern, not this class's. Whether the camera follows anyone is its
/// host's business.
/// </remarks>
public sealed class MapCamera
{
    private const int BaseTileSizePixels = 36;

    private static readonly Dictionary<ZoomLevel, Point> TileSizesByZoomLevel = new()
    {
        [ZoomLevel.Team] = new Point(BaseTileSizePixels, BaseTileSizePixels),
        [ZoomLevel.Neighborhood] = new Point(BaseTileSizePixels / 2, BaseTileSizePixels / 2),
        [ZoomLevel.Borough] = new Point(BaseTileSizePixels / 4, BaseTileSizePixels / 4),
    };

    private readonly IMapViewQuery _mapView;

    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;
    private Point _currentTileSize;
    private int _tileColumns;
    private int _tileRows;
    private Point _currentScrollPosition;
    private Point _minScrollPosition;
    private Point _maxScrollPosition;
    private Vector2 _renderPixelOffset;
    private Point _rightDragStartScrollPosition;

    /// <summary>The map bounds the scroll limits were last computed from -- the sliding window changes them as neighborhoods load and unload.</summary>
    private MapBounds _scrollLimitBounds;

    public MapCamera(IMapViewQuery mapView)
    {
        _mapView = mapView;
    }

    public Point CurrentTileSize => _currentTileSize;
    public int TileColumns => _tileColumns;
    public int TileRows => _tileRows;
    public Point CurrentScrollPosition => _currentScrollPosition;
    public Vector2 RenderPixelOffset => _renderPixelOffset;
    public ZoomLevel CurrentZoomLevel => _currentZoomLevel;

    public void Initialize(Vector2 contentSize)
    {
        UpdateTileSizes(contentSize);
        UpdateScrollLimits();
    }

    /// <summary>The screen position of a tile's top-left corner, given its column/row within the visible viewport -- shared by every map draw method so _renderPixelOffset's smooth drag shift only ever needs applying in one place.</summary>
    public Vector2 TileOrigin(int columnIndex, int rowIndex) =>
        new Vector2(columnIndex * _currentTileSize.X, rowIndex * _currentTileSize.Y) - _renderPixelOffset;

    /// <summary>The on-screen rectangle of a map tile, or false when it is outside the visible grid.</summary>
    public bool TryGetTileRectangle(int mapX, int mapY, out Rectangle tileRectangle)
    {
        var column = mapX - _currentScrollPosition.X;
        var row = mapY - _currentScrollPosition.Y;

        if (column < 0 || column >= _tileColumns || row < 0 || row >= _tileRows)
        {
            tileRectangle = Rectangle.Empty;
            return false;
        }

        var origin = TileOrigin(column, row);
        tileRectangle = new Rectangle((int)origin.X, (int)origin.Y, _currentTileSize.X, _currentTileSize.Y);
        return true;
    }

    /// <summary>The screen position of a point in map tile coordinates, where a whole number is a tile's top-left corner.</summary>
    public Vector2 TileToScreen(Vector2 tilePosition) =>
        new Vector2((tilePosition.X - _currentScrollPosition.X) * _currentTileSize.X, (tilePosition.Y - _currentScrollPosition.Y) * _currentTileSize.Y) - _renderPixelOffset;

    /// <summary>Centres the viewport on position, returning how much the scroll position actually moved.</summary>
    /// <remarks>
    /// Returns the applied delta for the same reason UpdateScrollPosition/ApplyDrag/EndDrag do:
    /// the caller caches per-visible-tile state and needs to know exactly how far the grid shifted
    /// to update it incrementally rather than rebuilding wholesale. That matters most here --
    /// camera-follow calls this on every single player step, and a one-tile step used to trigger a
    /// full re-resolve of every visible cell's background. Clamping means the answer is often
    /// Point.Zero (the camera is already against the map edge, or the player moved along it), in
    /// which case the caller has nothing to do at all.
    /// </remarks>
    /// <param name="position">The map position to centre on.</param>
    /// <returns>The change actually applied to the scroll position, in tiles.</returns>
    public Point CenterCameraOn(Vector3Int position)
    {
        var previousScrollPosition = _currentScrollPosition;
        var desiredScroll = new Point(position.X - _tileColumns / 2, position.Y - _tileRows / 2);
        _currentScrollPosition = new Point(
            System.Math.Clamp(desiredScroll.X, _minScrollPosition.X, _maxScrollPosition.X),
            System.Math.Clamp(desiredScroll.Y, _minScrollPosition.Y, _maxScrollPosition.Y));

        _renderPixelOffset = Vector2.Zero;

        return new Point(_currentScrollPosition.X - previousScrollPosition.X, _currentScrollPosition.Y - previousScrollPosition.Y);
    }

    public void UpdateZoomLevel(ZoomLevel newZoomLevel, Vector2 contentSize)
    {
        _currentZoomLevel = newZoomLevel;
        UpdateTileSizes(contentSize);

        // Zooming changes how many tiles are visible, so the max scroll bound (computed
        // from the visible tile count) is now stale too -- and the current scroll position,
        // valid under the old bound, may now exceed the new one (e.g. zooming out after
        // scrolling far while zoomed in) and needs re-clamping.
        UpdateScrollLimits();
        _currentScrollPosition = new Point(
            System.Math.Clamp(_currentScrollPosition.X, _minScrollPosition.X, _maxScrollPosition.X),
            System.Math.Clamp(_currentScrollPosition.Y, _minScrollPosition.Y, _maxScrollPosition.Y));

        // A zoom mid-drag would otherwise leave a stale smooth-scroll offset sized for the old
        // tile size shifting the newly-resized grid.
        _renderPixelOffset = Vector2.Zero;
    }

    public void CycleZoom(int direction, Vector2 contentSize)
    {
        var zoomLevels = Enum.GetValues<ZoomLevel>();
        var currentIndex = Array.IndexOf(zoomLevels, _currentZoomLevel);
        var newIndex = System.Math.Clamp(currentIndex + direction, 0, zoomLevels.Length - 1);
        UpdateZoomLevel(zoomLevels[newIndex], contentSize);
    }

    /// <summary>Recomputes the scroll limits and keeps the scroll position inside them when the map's bounds have changed since they were last computed.</summary>
    /// <returns>Whether the bounds had changed.</returns>
    public bool RefreshScrollLimitsIfBoundsChanged()
    {
        if (_mapView.Bounds == _scrollLimitBounds)
        {
            return false;
        }

        UpdateScrollLimits();
        _currentScrollPosition = new Point(
            System.Math.Clamp(_currentScrollPosition.X, _minScrollPosition.X, _maxScrollPosition.X),
            System.Math.Clamp(_currentScrollPosition.Y, _minScrollPosition.Y, _maxScrollPosition.Y));
        return true;
    }

    /// <summary>Applies a clamped scroll delta and returns how much actually changed, so a caller can tell a clamped scroll that didn't move from one that did.</summary>
    public Point UpdateScrollPosition(Point scrollChange)
    {
        var previousScrollPosition = _currentScrollPosition;

        _currentScrollPosition = new Point(
            System.Math.Clamp(_currentScrollPosition.X + scrollChange.X, _minScrollPosition.X, _maxScrollPosition.X),
            System.Math.Clamp(_currentScrollPosition.Y + scrollChange.Y, _minScrollPosition.Y, _maxScrollPosition.Y));

        return new Point(_currentScrollPosition.X - previousScrollPosition.X, _currentScrollPosition.Y - previousScrollPosition.Y);
    }

    /// <summary>Snapshots the scroll position the moment a right-mouse-drag starts, so ApplyDrag always has a fixed anchor to measure the drag against.</summary>
    public void BeginDrag() => _rightDragStartScrollPosition = _currentScrollPosition;

    /// <summary>
    /// Given the total pixel delta since the drag started (not a per-frame increment), computes
    /// the smooth sub-tile render offset and returns the whole-tile scroll delta still needed to
    /// reach it -- deliberately does not commit that delta to CurrentScrollPosition itself; the
    /// caller applies it via UpdateScrollPosition so cache invalidation and scroll commitment
    /// stay in exactly one place.
    /// </summary>
    public Point ApplyDrag(Vector2 totalPixelDeltaSinceStart)
    {
        var continuousPixelPosition = new Vector2(
             MathHelper.Clamp(_rightDragStartScrollPosition.X * _currentTileSize.X - totalPixelDeltaSinceStart.X, _minScrollPosition.X * _currentTileSize.X, _maxScrollPosition.X * _currentTileSize.X),
             MathHelper.Clamp(_rightDragStartScrollPosition.Y * _currentTileSize.Y - totalPixelDeltaSinceStart.Y, _minScrollPosition.Y * _currentTileSize.Y, _maxScrollPosition.Y * _currentTileSize.Y));

        var wholeTileScroll = new Point(
            (int)System.Math.Floor(continuousPixelPosition.X / _currentTileSize.X),
            (int)System.Math.Floor(continuousPixelPosition.Y / _currentTileSize.Y));

        _renderPixelOffset = new Vector2(
            continuousPixelPosition.X - wholeTileScroll.X * _currentTileSize.X,
            continuousPixelPosition.Y - wholeTileScroll.Y * _currentTileSize.Y);

        return new Point(wholeTileScroll.X - _currentScrollPosition.X, wholeTileScroll.Y - _currentScrollPosition.Y);
    }

    /// <summary>Settles the smooth sub-tile scroll offset onto the tile grid once a drag gesture ends, returning the whole-tile snap still needed (again left for the caller to commit via UpdateScrollPosition).</summary>
    public Point EndDrag()
    {
        if (_renderPixelOffset == Vector2.Zero)
        {
            return Point.Zero;
        }

        var snap = new Point(
            _renderPixelOffset.X >= _currentTileSize.X / 2f ? 1 : 0,
            _renderPixelOffset.Y >= _currentTileSize.Y / 2f ? 1 : 0);

        _renderPixelOffset = Vector2.Zero;

        return snap;
    }

    /// <summary>Shared mouse-to-map-tile math -- used both for click-to-select and for hover tracking while an ability is armed.</summary>
    public bool TryGetHoveredMapPosition(Point mousePosition, Vector2 contentAbsolutePosition, out Point mapPosition)
    {
        var relativeMapDisplayMousePosition = new Vector2(mousePosition.X - contentAbsolutePosition.X, mousePosition.Y - contentAbsolutePosition.Y) + _renderPixelOffset;
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
        if (!_mapView.IsOnMap(new Vector3Int(mapPosition.X, mapPosition.Y, 0)))
        {
            mapPosition = default;
            return false;
        }

        return true;
    }

    private void UpdateTileSizes(Vector2 contentSize)
    {
        _currentTileSize = TileSizesByZoomLevel[_currentZoomLevel];

        // +2 to account for partial tile rendering and scrolling jitter
        _tileColumns = (int)Math.Floor(contentSize.X / _currentTileSize.X) + 2;
        _tileRows = (int)Math.Floor(contentSize.Y / _currentTileSize.Y) + 2;
    }

    private void UpdateScrollLimits()
    {
        // Never below the minimum: a map smaller than the viewport has nowhere to scroll, not a
        // negative amount to scroll -- Math.Clamp(current, min, max) requires max >= min.
        var bounds = _mapView.Bounds;
        _scrollLimitBounds = bounds;
        _minScrollPosition = new Point(bounds.MinX, bounds.MinY);
        _maxScrollPosition = new Point(
            Math.Max(bounds.MinX, bounds.MaxX - _tileColumns),
            Math.Max(bounds.MinY, bounds.MaxY - _tileRows));
    }
}
