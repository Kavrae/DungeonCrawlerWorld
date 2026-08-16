using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// The per-visible-tile background color cache MapWindow draws every frame -- a flat,
/// column-major array (matching TileRenderer.DrawBackgrounds' expected layout) sized to
/// MapCamera's current tile grid. Resize/Reset are full rebuilds (camera tile-grid or map-layer
/// changes); ApplyScroll shifts already-known cells into their new positions and only
/// re-resolves the newly-exposed columns/rows, so panning doesn't re-resolve the whole visible
/// grid on every scroll step.
/// </summary>
public sealed class MapBackgroundCache(
    World world,
    MapViewState mapViewState,
    DirectComponentPool<BackgroundComponent> backgroundPool,
    MapCamera camera)
{
    private Color[] _colors = [];

    public Color[] Colors => _colors;

    /// <summary>Reallocates the cache to match MapCamera's current tile grid -- called whenever the camera's tile columns/rows change (Initialize, zoom). Leaves the new cells unresolved; callers follow up with Reset.</summary>
    public void Resize()
    {
        _colors = new Color[camera.TileColumns * camera.TileRows];
    }

    public void Reset()
    {
        for (var column = 0; column < camera.TileColumns; column++)
        {
            for (var row = 0; row < camera.TileRows; row++)
            {
                var mapNodeX = column + camera.CurrentScrollPosition.X;
                var mapNodeY = row + camera.CurrentScrollPosition.Y;
                _colors[column + row * camera.TileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
            }
        }
    }

    /// <summary>
    /// Shifts already-known cells into their new positions and only re-resolves the
    /// newly-exposed columns/rows, instead of recomputing the whole visible grid on every
    /// scroll step.
    /// </summary>
    public void ApplyScroll(int scrollDeltaX, int scrollDeltaY)
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
        if (System.Math.Abs(scrollDeltaX) >= camera.TileColumns || System.Math.Abs(scrollDeltaY) >= camera.TileRows)
        {
            Reset();
            return;
        }

        var shiftedColorCache = new Color[camera.TileColumns * camera.TileRows];

        for (var columnIndex = 0; columnIndex < camera.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < camera.TileRows; rowIndex++)
            {
                var scrollColumn = columnIndex + scrollDeltaX;
                var scrollRow = rowIndex + scrollDeltaY;

                if (scrollColumn >= 0 && scrollColumn < camera.TileColumns && scrollRow >= 0 && scrollRow < camera.TileRows)
                {
                    shiftedColorCache[columnIndex + rowIndex * camera.TileColumns] = _colors[scrollColumn + scrollRow * camera.TileColumns];
                }
            }
        }

        _colors = shiftedColorCache;

        if (scrollDeltaX > 0)
        {
            for (var column = camera.TileColumns - scrollDeltaX; column < camera.TileColumns; column++)
            {
                FillColumn(column);
            }
        }
        else if (scrollDeltaX < 0)
        {
            for (var column = 0; column < -scrollDeltaX; column++)
            {
                FillColumn(column);
            }
        }

        if (scrollDeltaY > 0)
        {
            for (var row = camera.TileRows - scrollDeltaY; row < camera.TileRows; row++)
            {
                FillRow(row);
            }
        }
        else if (scrollDeltaY < 0)
        {
            for (var row = 0; row < -scrollDeltaY; row++)
            {
                FillRow(row);
            }
        }
    }

    private void FillColumn(int column)
    {
        var mapNodeX = column + camera.CurrentScrollPosition.X;
        for (var row = 0; row < camera.TileRows; row++)
        {
            var mapNodeY = row + camera.CurrentScrollPosition.Y;
            _colors[column + row * camera.TileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
        }
    }

    private void FillRow(int row)
    {
        var mapNodeY = row + camera.CurrentScrollPosition.Y;
        for (var column = 0; column < camera.TileColumns; column++)
        {
            var mapNodeX = column + camera.CurrentScrollPosition.X;
            _colors[column + row * camera.TileColumns] = ResolveBackgroundColor(mapNodeX, mapNodeY);
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
        if (!world.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)))
        {
            return Color.Black;
        }

        var currentMapLayer = mapViewState.CurrentMapLayer;

        var occupantEntityId = world.Map.GetBlockingEntityId(new Vector3Int(mapNodeX, mapNodeY, currentMapLayer));
        if (occupantEntityId != -1 && backgroundPool.TryGetReadonly(occupantEntityId, out var occupantBackground))
        {
            return occupantBackground.BackgroundColor;
        }

        if (Map.TerrainLayerFor(currentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = world.Map.GetTerrainEntityId(mapNodeX, mapNodeY, terrainLayer);
            return terrainEntityId != -1 && backgroundPool.TryGetReadonly(terrainEntityId, out var terrainBackground)
                ? terrainBackground.BackgroundColor
                : Color.White;
        }

        return Color.White;
    }
}
