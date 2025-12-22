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

    public struct RaceComponent(Guid id, string name, string description) : IEntityComponent
    {
        public Guid Id { get; } = id;
        public string Name { get; set; } = name;
        public string Description { get; set; } = description;
    }
}
