using System;

namespace DungeonCrawlerWorld.Components
{    /// <summary>
    /// A core component that specifies the class details of an entity.
    /// Currently, these are primarily display and narrative properties, but will later include class level progression, abilities, passives, requirements, etc
    /// </summary>
    ///<todo> 
    /// ClassComponent OnRemoval custom removal to do things like remove abilities and stats. Ex : subclass and FormerChildActor
    /// Requirements
    /// Passive bonuses
    /// Active abilities
    /// Level progression
    ///</todo>
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
