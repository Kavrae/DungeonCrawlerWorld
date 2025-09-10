using System;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
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
