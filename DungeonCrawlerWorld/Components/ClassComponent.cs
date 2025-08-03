using System;

//TODO ClassComponent on removal custom removal to do things like removal abilities/stats? Ex : former child actor
namespace DungeonCrawlerWorld.Components
{
    public abstract class ClassComponent : IEntityComponent
    {
        public Guid EntityId  { get; set; }
        public virtual Guid ClassId { get; }
        public string Name { get; set; }
        public string Description  { get; set; }

        public void Build(Guid entityId)
        {
            EntityId = entityId;

            ComponentRepo.AddClass(entityId, this );
            
            if (!ComponentRepo.DisplayTextComponents.TryGetValue(entityId, out DisplayTextComponent displayTextComponent))
            {
                displayTextComponent = new DisplayTextComponent(entityId);
            }
            displayTextComponent.ClassName += Name; //TODO format
            displayTextComponent.ClassDescription += Description; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
