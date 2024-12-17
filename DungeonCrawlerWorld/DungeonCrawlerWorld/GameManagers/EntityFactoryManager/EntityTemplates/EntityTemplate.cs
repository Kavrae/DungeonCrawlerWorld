using System;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class EntityTemplate
    {
        public Guid EntityId { get; set; }

        public EntityTemplate ()
        {
            EntityId = ComponentRepo.NewEntity();
        }
    }
}
