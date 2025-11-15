using System;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that specifies the racial details of an entity.
    /// These are primarily display and narrative properties, but the race component will be frequently used by various systems.
    /// </summary>
    ///<todo> 
    /// RaceComponent OnRemoval custom removal to do things like remove abilities and stats. Ex : changing race on floor 3
    ///</todo>
    public abstract class RaceComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public virtual Guid RaceId { get; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string PersonalName { get; set; }
        
        public void Build( int entityId )
        {
            EntityId = entityId;

            ComponentRepo.AddRace(entityId, this);
            
            var displayTextComponent = new DisplayTextComponent(entityId);
            displayTextComponent.Name += PersonalName; //TODO format
            displayTextComponent.RaceName += Name; //TODO format
            displayTextComponent.RaceDescription += Description; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
