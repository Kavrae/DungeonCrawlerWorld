using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Dirt : IBlueprint
    {
        
        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

       public Dirt()
        {
            EntityId = Guid.NewGuid();
            Name = "Dirt";
            Description = "Ordinary dirt. Nothing special.";

            _ = new BackgroundComponent(EntityId, Color.Tan);
            _ = new DisplayTextComponent(EntityId, Name, Description);
            _ = new TransformComponent(EntityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
