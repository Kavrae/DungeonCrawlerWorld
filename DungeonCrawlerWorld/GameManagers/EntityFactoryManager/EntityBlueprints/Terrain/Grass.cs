using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Grass : IBlueprint
    {
        
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

       public Grass()
        {
            EntityId = ComponentRepo.GetNextEntityId();
            Name = "Grass";
            Description = "Ordinary grass. Nothing special.";

            _ = new GlyphComponent(EntityId, ",", Color.LawnGreen, new Point(5, -2));
            _ = new BackgroundComponent(EntityId, Color.ForestGreen);
            _ = new DisplayTextComponent(EntityId, Name, Description);
            _ = new TransformComponent(EntityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
