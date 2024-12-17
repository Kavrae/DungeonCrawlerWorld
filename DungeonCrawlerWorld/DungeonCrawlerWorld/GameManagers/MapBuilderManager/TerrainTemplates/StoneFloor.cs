using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
    public class StoneFloor : EntityTemplate
    {
        public StoneFloor() : base()
        {
            _ = new BackgroundComponent(EntityId, Color.LightGray);

            _ = new GlyphComponent(EntityId, null, Color.LightGray, new Point(0, 0));

            _ = new DisplayTextComponent(EntityId, "Stone Floor", "Smooth stone floor.");
        }
    }
}
