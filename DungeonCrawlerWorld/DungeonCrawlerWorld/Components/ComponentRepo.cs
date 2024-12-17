using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Components
{
    public static class ComponentRepo
    {
        public static HashSet<Guid> Entities;

        public static Dictionary<Guid, EnergyComponent> ActionEnergyComponents;
        public static Dictionary<Guid, CollisionComponent> CollisionComponents;
        public static Dictionary<Guid, BackgroundComponent> DisplayBackgroundComponents;
        public static Dictionary<Guid, GlyphComponent> DisplayGlyphComponents;
        public static Dictionary<Guid, DisplayTextComponent> DisplayTextComponents;
        public static Dictionary<Guid, MovementComponent> MovementComponents;
        public static Dictionary<Guid, TransformComponent> TransformComponents;

        static ComponentRepo()
        {
            Entities = new HashSet<Guid>();

            ActionEnergyComponents = new Dictionary<Guid, EnergyComponent>();
            CollisionComponents = new Dictionary<Guid, CollisionComponent>();
            DisplayBackgroundComponents = new Dictionary<Guid, BackgroundComponent>();
            DisplayGlyphComponents = new Dictionary<Guid, GlyphComponent>();
            DisplayTextComponents = new Dictionary<Guid, DisplayTextComponent>();
            MovementComponents = new Dictionary<Guid, MovementComponent>();
            TransformComponents = new Dictionary<Guid, TransformComponent>();
        }

        public static Guid NewEntity()
        {
            var entityId = Guid.NewGuid();

            if (!Entities.Contains(entityId))
            {
                Entities.Add(entityId);
            }
            return entityId;
        }
    }
}
