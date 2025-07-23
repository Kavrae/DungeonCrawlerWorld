using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Wall : IBlueprint
    {
        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public Wall()
        {
            EntityId = Guid.NewGuid();
            Name = "Wall";
            Description = "Basic wall. Default implementation.";

            _ = new DisplayTextComponent(EntityId, Name, Description);
            _ = new GlyphComponent(EntityId, "[][]", Color.DarkGray, new Point(0, -1));
            _ = new TransformComponent(EntityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
