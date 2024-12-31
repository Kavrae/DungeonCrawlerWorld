using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Wall : IBaseFactoryTemplate
    {
        private static string Name => "Wall";

        private static string Description => "Basic wall. Default implementation.";
        
        public override void Build(Guid entityId)
        {
            _ = new DisplayTextComponent(entityId, Name, Description);
            _ = new GlyphComponent(entityId, "[][]", Color.DarkGray, new Point(0, -1));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
