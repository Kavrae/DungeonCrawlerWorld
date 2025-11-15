namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that contains the text information displayed whenever a user selects a mapNode containing this entity.
    /// </summary>
    /// <todo>
    /// Split descriptions into various forms that display based on the user's various skills.
    /// Retrieve data form data storage.  Pre-load for entities that may be interacted with. Persist for previously interacted entities.
    /// </todo>
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
