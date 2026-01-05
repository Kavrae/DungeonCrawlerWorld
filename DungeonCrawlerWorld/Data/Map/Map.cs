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

            int xCoordinate;
            int yCoordinate;
            int zCoordinate;

            for (xCoordinate = 0; xCoordinate < Size.X; xCoordinate++)
            {
                for (yCoordinate = 0; yCoordinate < Size.Y; yCoordinate++)
                {
                    for (zCoordinate = 0; zCoordinate < Size.Z; zCoordinate++)
                    {
                        MapNodes[xCoordinate, yCoordinate, zCoordinate] = new MapNode(xCoordinate, yCoordinate, zCoordinate);
                    }
                }
            }
        }

        public MapNode GetMapNode(Vector3Int coordinates)
        {
            return MapNodes[coordinates.X, coordinates.Y, coordinates.Z];
        }
    }
}
