using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    /// <summary>
    /// Represents a blueprint for creating game entities with a pre-defined component list and properties
    /// </summary>
    public interface IBlueprint
    {
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
