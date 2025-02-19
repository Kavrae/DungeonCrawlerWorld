using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class StoneFloor : IBaseFactoryTemplate
    {
        public override string Name => "Stone floor";

        public override string Description => "Roughly shaped stone floor.";

        public override void Build(Guid entityId)
        {
            _ = new BackgroundComponent(entityId, Color.LightGray);
            _ = new DisplayTextComponent(entityId, Name, Description);
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
