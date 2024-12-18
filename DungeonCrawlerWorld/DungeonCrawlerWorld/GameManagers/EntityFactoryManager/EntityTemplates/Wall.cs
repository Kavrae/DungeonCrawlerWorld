using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Wall : EntityTemplate
    {
        public Wall() : base()
        {
            _ = new DisplayTextComponent(EntityId, "Wall (Default)", "Basic wall. Default implementation.");

            _ = new GlyphComponent(EntityId, "[][]", Color.DarkGray, new Point (0, -1));
        }
    }
}
