using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Grass : IBaseFactoryTemplate
    {
        public override string Name => "Grass";

        public override string Description => "Ordinary grass. Nothing special.";

        public override void Build(Guid entityId)
        {
            _ = new GlyphComponent(entityId, ",", Color.LawnGreen, new Point(5, -2));
            _ = new BackgroundComponent(entityId, Color.ForestGreen);
            _ = new DisplayTextComponent(entityId, Name, Description);
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
