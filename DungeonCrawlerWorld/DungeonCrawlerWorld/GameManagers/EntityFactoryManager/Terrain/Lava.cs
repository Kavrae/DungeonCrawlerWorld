using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Lava : IBaseFactoryTemplate
    {
        private static string Name => "Lava";

        private static string Description => "Hot lava. I do not recommend stepping on it.";

        public override void Build(Guid entityId)
        {
            _ = new GlyphComponent(entityId, "~", Color.Yellow, new Point(3, 0));
            _ = new BackgroundComponent(entityId, Color.OrangeRed);
            _ = new DisplayTextComponent(entityId, Name, Description);
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
