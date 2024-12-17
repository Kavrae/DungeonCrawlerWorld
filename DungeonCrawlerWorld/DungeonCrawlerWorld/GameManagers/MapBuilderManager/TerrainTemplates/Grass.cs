using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.MapBuilderManager
{
    public class Grass : EntityTemplate
    {
        public Grass() : base()
        {
            _ = new BackgroundComponent(EntityId, Color.ForestGreen);

            _ = new GlyphComponent(EntityId, ",", Color.LawnGreen, new Point(5, -2));

            _ = new DisplayTextComponent(EntityId, "Grass", "Ordinary grass. Nothing special.");
        }
    }
}
