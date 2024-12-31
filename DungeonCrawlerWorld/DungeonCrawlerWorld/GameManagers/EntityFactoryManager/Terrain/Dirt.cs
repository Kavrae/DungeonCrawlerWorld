using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Dirt : IBaseFactoryTemplate
    {
        private static string Name => "Dirt";

        private static string Description => "Ordinary dirt. Nothing special.";

        public override void Build(Guid entityId)
        {
            _ = new BackgroundComponent(entityId, Color.Tan);
            _ = new DisplayTextComponent(entityId, Name, Description);
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
