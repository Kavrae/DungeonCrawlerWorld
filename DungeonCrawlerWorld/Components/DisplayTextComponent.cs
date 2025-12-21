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
        public string Name { get; set; }
        public string Description { get; set; }

        public DisplayTextComponent(int entityId) : this(entityId, "", "") { }

        public DisplayTextComponent(int entityId, string name, string description)
        {
            Name = name;
            Description = description;

            ComponentRepo.DisplayTextComponents[entityId] = this;
        }

        public override string ToString()
        {
            return "Description";
        }
    }
}
