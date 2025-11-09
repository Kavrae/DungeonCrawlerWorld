using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public interface IBlueprint
    {
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
