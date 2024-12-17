using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
    public class Lava : EntityTemplate
    {
        public Lava() : base()
        {
            _ = new BackgroundComponent(EntityId, Color.OrangeRed);

            _ = new GlyphComponent(EntityId, "~", Color.Yellow, new Point(3, 0));

            _ = new DisplayTextComponent(EntityId, "Lava", "Hot lava. I do not recommend stepping on it.");
        }
    }
}
