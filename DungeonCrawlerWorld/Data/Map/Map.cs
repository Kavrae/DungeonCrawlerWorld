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
                        MapNodes[x, y, z] = new MapNode(x, y, z, mapSize);
                    }
                }
            }
        }
    }
}
