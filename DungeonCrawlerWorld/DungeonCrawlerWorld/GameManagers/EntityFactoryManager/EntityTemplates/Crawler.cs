using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Crawler : EntityTemplate
    {
        public Crawler() : base()
        {
            _ = new DisplayTextComponent(EntityId, "Crawler (Default)", "Crawler entity. Default implementation.");

            _ = new GlyphComponent(EntityId, "O", Color.Black, new Point(2, 0));

            _ = new EnergyComponent(EntityId, 0, 10, 100);

            _ = new MovementComponent(EntityId, MovementMode.SeekTarget, 60);
        }
    }
}
