using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>What a cached map image shows: the visible grid's top-left map tile, its size in tiles and pixels per tile, and the map layer.</summary>
/// <remarks>A cached image whose key differs from the camera's current one is stale, whatever moved the camera. A drag's sub-tile offset is not part of it: that is where the image is blitted, not what it shows.</remarks>
public readonly record struct MapImageKey(Point Scroll, int TileColumns, int TileRows, Point TileSize, int MapLayer)
{
    public static MapImageKey Of(MapCamera camera, int mapLayer) =>
        new(camera.CurrentScrollPosition, camera.TileColumns, camera.TileRows, camera.CurrentTileSize, mapLayer);
}
