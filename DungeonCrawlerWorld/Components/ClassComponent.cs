using System;

//TODO ClassComponent on removal custom removal to do things like removal abilities/stats? Ex : former child actor
namespace DungeonCrawlerWorld.Components
{
    public abstract class ClassComponent
    {
        public Guid EntityId  { get; set; }
        public string Name  { get; set; }
        public string Description  { get; set; }

        public ClassComponent(Guid entityId)
        {
            EntityId = entityId;

            ComponentRepo.ClassComponents.Add(entityId, this );
        }
    }
}
