using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Tank : ClassComponent
    {
        public Tank(Guid entityId)
        {
            Name = "Tank";
            Description = "Extra hit points";

            _ = new ClassGlyphComponent(entityId, "t", Color.BlueViolet, new Point(0, 0));

            //TODO temporary : tanks have 10% more health and health regen
            if (ComponentRepo.HealthComponents.TryGetValue(EntityId, out var healthComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiple them here. Otherwise order matters.
                healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
                healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
                ComponentRepo.HealthComponents[EntityId] = healthComponent;
            }

            base.Build(entityId);
        }
    }
}
