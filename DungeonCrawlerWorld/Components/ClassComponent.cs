using System;

//TODO ClassComponent on removal custom removal to do things like removal abilities/stats? Ex : former child actor
namespace DungeonCrawlerWorld.Components
{
    public abstract class ClassComponent : IEntityComponent
    {
        public int EntityId  { get; set; }
        public virtual Guid ClassId { get; }
        public string Name { get; set; }
        public string Description  { get; set; }

        public void Build(int entityId)
        {
            EntityId = entityId;

            ComponentRepo.AddClass(entityId, this );
            
            var displayTextComponent = new DisplayTextComponent(entityId);
            displayTextComponent.ClassName += Name; //TODO format
            displayTextComponent.ClassDescription += Description; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
