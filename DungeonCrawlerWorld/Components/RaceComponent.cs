using System;

//TODO RaceComponent on removal custom removal to do things like removal abilities/stats? Ex : changing race on floor 3
namespace DungeonCrawlerWorld.Components
{
    public abstract class RaceComponent : IEntityComponent
    {
        public Guid EntityId { get; set; }
        public virtual Guid RaceId { get; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string PersonalName { get; set; }
        
        public void Build( Guid entityId )
        {
            EntityId = entityId;

            ComponentRepo.AddRace(entityId, this);

            if (!ComponentRepo.DisplayTextComponents.TryGetValue(entityId, out DisplayTextComponent displayTextComponent))
            {
                displayTextComponent = new DisplayTextComponent(entityId);
            }
            displayTextComponent.Name += PersonalName; //TODO format
            displayTextComponent.RaceName += Name; //TODO format
            displayTextComponent.RaceDescription += Description; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
