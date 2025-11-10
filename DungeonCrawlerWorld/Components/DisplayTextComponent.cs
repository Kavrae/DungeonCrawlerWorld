namespace DungeonCrawlerWorld.Components
{
    public struct DisplayTextComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string RaceName { get; set; }
        public string RaceDescription { get; set; }

        public string ClassName { get; set; }
        public string ClassDescription { get; set; }

        public DisplayTextComponent(int entityId) : this(entityId, "", "", "", "", "", "") { }
        public DisplayTextComponent(int entityId, string name, string description) : this(entityId, name, description, "", "", "", "") { }

        //TODO remove race and class specific strings. Replace them with arrays that can be formatted
        //TODO when creating a race/class, what if a display text component already exists?  Update it instead.
        public DisplayTextComponent(int entityId, string name, string description, string raceName, string raceDescription, string className, string classDescription)
        {
            EntityId = entityId;
            Name = name;
            Description = description;

            RaceName = raceName;
            RaceDescription = raceDescription;

            ClassName = className;
            ClassDescription = classDescription;

            ComponentRepo.DisplayTextComponents[entityId] = this;
        }

        public override string ToString()
        {
            return "Description";
        }
    }
}
