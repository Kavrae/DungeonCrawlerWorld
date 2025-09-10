using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Tank : ClassComponent
    {
        public override Guid ClassId => new("1a260ce6-6dd7-4168-8669-aaef1e98b565");

        public Tank(int entityId)
        {
            Name = "Tank";
            Description = "Extra hit points";

            //TODO temporary : tanks have 10% more health and health regen
            if (ComponentRepo.HealthComponents.TryGetValue(EntityId, out var healthComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiply them here. Otherwise order matters and introduces many bugs.
                healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
                healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
                ComponentRepo.HealthComponents[EntityId] = healthComponent;
            }

            base.Build(entityId);
        }
    }
}
