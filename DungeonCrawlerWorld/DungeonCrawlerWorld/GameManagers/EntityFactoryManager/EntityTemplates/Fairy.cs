using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Fairy : EntityTemplate
    {
        public Fairy() : base()
        {
            _ = new DisplayTextComponent(EntityId, "Fairy (Default)", "Fairy entity. Default implementation. Flies around the map quickly and haphazardly");

            _ = new GlyphComponent(EntityId, "f", Color.DeepPink, new Point(4, 0));

            _ = new EnergyComponent(EntityId, 0, 1, 100);

            _ = new MovementComponent(EntityId, MovementMode.Random, 20);

            _ = new CollisionComponent(EntityId, ColliderType.Standard); ;
        }
    }
}
