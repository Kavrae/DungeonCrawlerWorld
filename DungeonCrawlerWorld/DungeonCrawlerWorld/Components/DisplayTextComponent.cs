using System;

namespace DungeonCrawlerWorld.Components
{
    public struct DisplayTextComponent
    {
        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public DisplayTextComponent(Guid entityId) : this(entityId, "", "") { }
        public DisplayTextComponent(Guid entityId, string name, string description)
        {
            EntityId = entityId;
            Name = name;
            Description = description;

            ComponentRepo.DisplayTextComponents.Remove(entityId);
            ComponentRepo.DisplayTextComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return "Description";
        }
    }
}
