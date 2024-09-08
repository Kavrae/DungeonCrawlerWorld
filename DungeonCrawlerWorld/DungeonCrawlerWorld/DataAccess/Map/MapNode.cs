using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Data
{
    public class MapNode
    {
        public Point Position;
        public bool IsSelected;
        public Entity Terrain;
        public List<Entity> Entities;

        public MapNode() 
        {
            Entities = new List<Entity>();
        }

        public void AddEntity(Entity newEntity)
        {
            Entities.Add(newEntity);
        }
    }
}
