using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public interface IBlueprint
    {
        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
