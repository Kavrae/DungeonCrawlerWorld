using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    /// <summary>
    /// The in-memory game map consisting of a 3d grid of map nodes
    /// </summary>
    public class Map
    {
        /// <summary>
        /// The current size of the map in mapNodes.
        /// </summary>
        /// <value></value>
        public Vector3Int Size { get; set; }

        /// <summary>
        /// A 3d grid of mapNodes, represented by the X,Y, and Z coordinates
        /// MapNode [0,0,0] is drawn to the top left of the map window.
        /// </summary>
        public MapNode[,,] MapNodes { get; set; }

        /// <summary>
        /// Instantiates a new map and array of mapNodes of a specified size.
        /// All mapNodes must be initialized during creation to avoid constant null reference checks during updates.
        /// </summary>
        /// <todo>
        /// Pull the map from save data
        /// </todo>
        public Map(Vector3Int mapSize)
        {
            Size = mapSize;
            MapNodes = new MapNode[mapSize.X, mapSize.Y, mapSize.Z];

            //Direct array indexing is found to be more performant over other iterators for larger maps.
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
