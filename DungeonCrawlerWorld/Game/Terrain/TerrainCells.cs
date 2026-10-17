using Engine.Math;
using Game.World;

namespace Game.Terrain;

/// <summary>Walks the loaded map's terrain cells.</summary>
public static class TerrainCells
{
    /// <summary>Calls visit with the position of every loaded floor cell holding terrain typeId, on every MapLayer that has a floor.</summary>
    /// <remarks>A scan of the whole loaded map, for something rare: a terrain's definition changing during a session.</remarks>
    public static void ForEachOfType(IMapQuery map, ushort typeId, Action<Vector3Int> visit)
    {
        var bounds = map.Bounds;
        for (var mapLayer = 0; mapLayer < bounds.Depth; mapLayer++)
        {
            if (Map.TerrainLayerFor(mapLayer) is null)
            {
                continue;
            }

            for (var y = bounds.MinY; y < bounds.MaxY; y++)
            {
                for (var x = bounds.MinX; x < bounds.MaxX; x++)
                {
                    var position = new Vector3Int(x, y, mapLayer);
                    if (!map.IsOnMap(position))
                    {
                        x = System.Math.Min(bounds.MaxX, Neighborhoods.OriginOf(Neighborhoods.CellOf(x) + 1)) - 1;
                        continue;
                    }

                    if (map.GetTerrainAt(position).TypeId == typeId)
                    {
                        visit(position);
                    }
                }
            }
        }
    }
}
