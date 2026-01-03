using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    /// <summary>
    /// Represents a single position on a 3 dimensional map
    /// Positions are specified as a Vector3Int, rather than a Vector3, to use them as array indexes without casting.
    /// An EntityId of -1 indicates no entity (sentinal value).
    /// </summary>
    /// <remarks>
    /// Instantiates a new mapNode at a specified 3d coordinate.
    /// </remarks>
    public struct MapNode(int x, int y, int z)
    {
        public Vector3Int Position { get; set; } = new Vector3Int(x, y, z);

        public int EntityId { get; set; } = -1;
    }
}
