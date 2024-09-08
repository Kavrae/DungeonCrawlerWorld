using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data
{
    public class Map
    {
        public Vector2 Size;
        public MapNode[,] Nodes;
        public MapNode SelectedMapNode;

        public Map(Vector2 mapSize)
        {
            this.Size = mapSize;
            Nodes = new MapNode[(int)mapSize.X, (int)mapSize.Y];
            SelectedMapNode = null;
        }
    }
}
