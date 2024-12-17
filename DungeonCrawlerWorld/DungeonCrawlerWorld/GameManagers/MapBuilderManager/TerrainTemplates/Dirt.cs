using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
    public class Dirt : EntityTemplate
    {
        public Dirt() : base()
        {
            _ = new BackgroundComponent(EntityId, Color.Tan);

            _ = new GlyphComponent(EntityId, null, Color.Tan, new Point(0, 0));

            _ = new DisplayTextComponent(EntityId, "Dirt", "Ordinary dirt. Nothing special.");
        }
    }
}
