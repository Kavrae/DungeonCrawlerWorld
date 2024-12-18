using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Goblin : EntityTemplate
    {
        public Goblin() : base()
        {
            _ = new DisplayTextComponent(EntityId, "Goblin", "Fast and green. Oddly fascinated with pineapples");

            _ = new GlyphComponent(EntityId, "g", Color.DarkGreen, new Point(3, -2));

            _ = new EnergyComponent(EntityId, 0, 10, 100);

            _ = new MovementComponent(EntityId, MovementMode.SeekTarget, 60);
        }
    }
}
