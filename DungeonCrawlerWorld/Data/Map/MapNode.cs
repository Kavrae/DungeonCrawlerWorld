using System;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    /// <summary>
    /// Represents a single position on a 3 dimensional map
    /// Contains references to its neighboring mapNodes to greatly improve lookup performance.
    /// Positions are specified as a Vector3Int, rather than a Vector3, to use them as array indexes without casting.
    /// </summary>
    public struct MapNode
    {
        public Vector3Int Position { get; set; }
        public Vector3Int? NeighborNorth { get; set; }
        public Vector3Int? NeighborEast { get; set; }
        public Vector3Int? NeighborSouth { get; set; }
        public Vector3Int? NeighborWest { get; set; }
        public Vector3Int? NeighborUp { get; set; }
        public Vector3Int? NeighborDown { get; set; }


        public int? EntityId { get; set; }

        /// <summary>
        /// Instantiates a new mapNode at a specified 3d coordinate.
        /// The mapSize is specified to more efficiently avoid map bounds when calculating neighboring node references.
        /// </summary>
        public MapNode(int x, int y, int z, Vector3Int mapSize)
        {
            EntityId = null;

            Position = new Vector3Int(x, y, z);
            NeighborNorth = y > 0 ? new Vector3Int(x, y - 1, z) : null;
            NeighborSouth = y < mapSize.X - 1 ? new Vector3Int(x, y + 1, z) : null;
            NeighborWest = x > 0 ? new Vector3Int(x - 1, y, z) : null;
            NeighborEast = x < mapSize.Y - 1 ? new Vector3Int(x + 1, y, z) : null;
            NeighborDown = z > 0 ? new Vector3Int(x, y, z + 1) : null;
            NeighborUp = z < mapSize.Z - 1 ? new Vector3Int(x, y, z - 1) : null;
        }
    }
}
