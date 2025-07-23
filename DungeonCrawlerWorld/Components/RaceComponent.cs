using System;

//TODO RaceComponent on removal custom removal to do things like removal abilities/stats? Ex : changing race on floor 3
namespace DungeonCrawlerWorld.Components
{
    public abstract class RaceComponent
    {
        public Guid EntityId  { get; set; }
        public string Name  { get; set; }
        public string Description  { get; set; }

        public RaceComponent(Guid entityId)
        {
            EntityId = entityId;

            ComponentRepo.RaceComponents.Add(entityId, this );
        }
    }
}
