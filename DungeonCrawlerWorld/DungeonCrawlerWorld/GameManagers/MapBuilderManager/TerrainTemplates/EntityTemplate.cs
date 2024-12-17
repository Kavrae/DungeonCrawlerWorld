using DungeonCrawlerWorld.Components;
using System;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
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
