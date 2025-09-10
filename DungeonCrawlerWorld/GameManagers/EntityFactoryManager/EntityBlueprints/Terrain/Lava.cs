using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Lava : IBlueprint
    {
        //TODO spawn lava in pools around the map
        //TODO stepping on lava deals damage every 10 frames + inflicts a stack of burn (up to x stacks), dealing damage every 10 frames for y triggers, resetting each time more fire is inflicted
        //TODO burn icon on selection

        
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

       public Lava()
        {
            EntityId = ComponentRepo.GetNextEntityId();
            Name = "Lava";
            Description = "Hot lava. I do not recommend stepping on it.";

            _ = new GlyphComponent(EntityId, "~", Color.Yellow, new Point(3, 0));
            _ = new BackgroundComponent(EntityId, Color.OrangeRed);
            _ = new DisplayTextComponent(EntityId, Name, Description);
            _ = new TransformComponent(EntityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
