using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class StoneFloor : IBlueprint
    {
        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

       public StoneFloor()
        {
            EntityId = ComponentRepo.GetNextEntityId();
            Name = "Stone floor";
            Description = "Roughly shaped stone floor.";
            
            _ = new BackgroundComponent(EntityId, Color.LightGray);
            _ = new DisplayTextComponent(EntityId, Name, Description);
            _ = new TransformComponent(EntityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
