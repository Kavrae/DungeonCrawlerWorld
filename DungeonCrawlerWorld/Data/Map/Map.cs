using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    public class Map
    {
        public Vector3Int Size { get; set; }

        public MapNode[,,] MapNodes { get; set; }
       
        public Map(Vector3Int mapSize)
        {
            Size = mapSize;
            MapNodes = new MapNode[mapSize.X, mapSize.Y, mapSize.Z];

            for (int x = 0; x < Size.X; x++)
            {
                for (int y = 0; y < Size.Y; y++)
                {
                    for (int z = 0; z < Size.Z; z++)
                    {
                        MapNodes[x, y, z] = new MapNode (new Vector3Int(x, y, z))
                        {
                            HasChanged = false,
                            Position = new Vector3Int(x, y, z),
                            NeighborNorth = y > 0 ? new Vector3Int(x, y - 1, z) : null,
                            NeighborSouth = y < mapSize.X - 1 ? new Vector3Int(x, y + 1, z) : null,
                            NeighborWest = x > 0 ? new Vector3Int(x - 1, y, z) : null,
                            NeighborEast = x < mapSize.Y - 1 ? new Vector3Int(x + 1, y, z) : null,
                            NeighborDown = z > 0 ? new Vector3Int(x, y, z + 1) : null,
                            NeighborUp = z < mapSize.Z - 1 ? new Vector3Int(x, y, z - 1) : null
                        };
                    }
                }
            }
        }
    }
}
