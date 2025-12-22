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
    public struct ClassComponent(Guid id, string name, string description) : IEntityComponent
    {
        public Guid Id { get; } = id;
        public string Name { get; set; } = name;
        public string Description { get; set; } = description;
    }
}
