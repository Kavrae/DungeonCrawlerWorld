using System;

namespace DungeonCrawlerWorld.Components
{
    public struct DisplayTextComponent
    {
        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string RaceName { get; set; }
        public string RaceDescription { get; set; }

        public string ClassName { get; set; }
        public string ClassDescription { get; set; }

        public DisplayTextComponent(Guid entityId) : this(entityId, "", "", "", "", "", "") { }
        public DisplayTextComponent(Guid entityId, string name, string description) : this(entityId, name, description, "", "", "", "") { }
        public DisplayTextComponent(Guid entityId, string name, string description, string raceName, string raceDescription, string className, string classDescription)
        {
            EntityId = entityId;
            Name = name;
            Description = description;

            RaceName = raceName;
            RaceDescription = raceDescription;

            ClassName = className;
            ClassDescription = classDescription;

            ComponentRepo.DisplayTextComponents.Remove(entityId);
            ComponentRepo.DisplayTextComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return "Description";
        }
    }
}
