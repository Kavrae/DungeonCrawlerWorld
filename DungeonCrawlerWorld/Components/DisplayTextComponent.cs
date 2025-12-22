namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that contains the text information displayed whenever a user selects a mapNode containing this entity.
    /// </summary>
    /// <todo>
    /// Split descriptions into various forms that display based on the user's various skills.
    /// Retrieve data form data storage.  Pre-load for entities that may be interacted with. Persist for previously interacted entities.
    /// </todo>
    public struct DisplayTextComponent(string name, string description) : IEntityComponent
    {
        public string Name { get; set; } = name;
        public string Description { get; set; } = description;

        public override string ToString()
        {
            return "Description";
        }
    }
}
