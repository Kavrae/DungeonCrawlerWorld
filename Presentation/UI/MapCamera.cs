using Engine.Math;
using Game.World;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// MapWindow's own scroll/zoom/pan viewport bookkeeping -- tile-space camera state entirely
/// local to a single MapWindow instance (nothing else reads it)
/// </summary>
/// <remarks>
/// Every method that repositions the camera (UpdateScrollPosition/ApplyDrag/EndDrag) returns
/// the actually-applied delta rather than reaching out to invalidate anything itself -- the
/// background-color cache these deltas drive is MapWindow's own rendering concern, not this
/// class's, so MapCamera stays ignorant of it and MapWindow's existing UpdateScrollPosition/
/// ResetBackgroundColorCache wrappers remain the single commit points.
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

    private readonly World _world;

    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;
    private Point _currentTileSize;
    private int _tileColumns;
    private int _tileRows;
    private Point _currentScrollPosition;
    private Point _maxScrollPosition;
    private Vector2 _renderPixelOffset;
    private bool _cameraFollowsPlayer = true;
    private Point _rightDragStartScrollPosition;

    public MapCamera(World world)
    {
        _world = world;
    }

    public Point CurrentTileSize => _currentTileSize;
    public int TileColumns => _tileColumns;
    public int TileRows => _tileRows;
    public Point CurrentScrollPosition => _currentScrollPosition;
    public Vector2 RenderPixelOffset => _renderPixelOffset;
    public bool FollowsPlayer => _cameraFollowsPlayer;

    /// <summary>The last map position the camera actively followed the player to -- MapWindow.Update compares against this every frame to notice player movement.</summary>
    public Vector3Int LastKnownPlayerPosition { get; set; }

    public void Initialize(Vector2 contentSize)
    {
        UpdateTileSizes(contentSize);
        UpdateMaxScrollPosition();
    }

    public void ResumeFollowingPlayer() => _cameraFollowsPlayer = true;

    /// <summary>The screen position of a tile's top-left corner, given its column/row within the visible viewport -- shared by every MapWindow draw method so _renderPixelOffset's smooth drag shift only ever needs applying in one place.</summary>
    public Vector2 TileOrigin(int columnIndex, int rowIndex) =>
        new Vector2(columnIndex * _currentTileSize.X, rowIndex * _currentTileSize.Y) - _renderPixelOffset;

    public void CenterCameraOn(Vector3Int position)
    {
        var desiredScroll = new Point(position.X - _tileColumns / 2, position.Y - _tileRows / 2);
        _currentScrollPosition = new Point(
            MathUtility.ClampInt(desiredScroll.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(desiredScroll.Y, 0, _maxScrollPosition.Y));

        _renderPixelOffset = Vector2.Zero;
    }

    public void UpdateZoomLevel(ZoomLevel newZoomLevel, Vector2 contentSize)
    {
        _currentZoomLevel = newZoomLevel;
        UpdateTileSizes(contentSize);

        // Zooming changes how many tiles are visible, so the max scroll bound (computed
        // from the visible tile count) is now stale too -- and the current scroll position,
        // valid under the old bound, may now exceed the new one (e.g. zooming out after
        // scrolling far while zoomed in) and needs re-clamping.
        UpdateMaxScrollPosition();
        _currentScrollPosition = new Point(
            MathUtility.ClampInt(_currentScrollPosition.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(_currentScrollPosition.Y, 0, _maxScrollPosition.Y));

        // A zoom mid-drag would otherwise leave a stale smooth-scroll offset sized for the old
        // tile size shifting the newly-resized grid.
        _renderPixelOffset = Vector2.Zero;
    }

    public void CycleZoom(int direction, Vector2 contentSize)
    {
        var zoomLevels = Enum.GetValues<ZoomLevel>();
        var currentIndex = Array.IndexOf(zoomLevels, _currentZoomLevel);
        var newIndex = MathUtility.ClampInt(currentIndex + direction, 0, zoomLevels.Length - 1);
        UpdateZoomLevel(zoomLevels[newIndex], contentSize);
    }

    /// <summary>Applies a clamped scroll delta and returns how much actually changed, so the caller can shift/rebuild whatever per-tile state it caches (e.g. MapWindow's background color cache) by exactly that amount instead of rebuilding it wholesale.</summary>
    public Point UpdateScrollPosition(Point scrollChange)
    {
        var previousScrollPosition = _currentScrollPosition;

        _currentScrollPosition = new Point(
            MathUtility.ClampInt(_currentScrollPosition.X + scrollChange.X, 0, _maxScrollPosition.X),
            MathUtility.ClampInt(_currentScrollPosition.Y + scrollChange.Y, 0, _maxScrollPosition.Y));

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
        if (!_world.IsOnMap(new Vector3Int(mapPosition.X, mapPosition.Y, 0)))
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
        _tileColumns = (int)System.Math.Floor(contentSize.X / _currentTileSize.X) + 2;
        _tileRows = (int)System.Math.Floor(contentSize.Y / _currentTileSize.Y) + 2;
    }

    private void UpdateMaxScrollPosition()
    {
        // Never negative: a map smaller than the viewport has nowhere to scroll, not a
        // negative amount to scroll -- ClampInt(current, 0, max) requires max >= 0.
        _maxScrollPosition = new Point(
            System.Math.Max(0, _world.Map.Size.X - _tileColumns),
            System.Math.Max(0, _world.Map.Size.Y - _tileRows));
    }
}
