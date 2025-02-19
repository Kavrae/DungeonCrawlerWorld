using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.ComponentSystems
{
    public class HealthRegenSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 10;

        public HealthRegenSystem() { }

        public void Update(GameTime gameTime)
        {
            foreach (var keyComponent in ComponentRepo.HealthComponents)
            {
                var healthComponent = keyComponent.Value;
                if (healthComponent.HealthRegen != 0)
                {
                    healthComponent.CurrentHealth += healthComponent.HealthRegen;
                    if( healthComponent.CurrentHealth > healthComponent.MaximumHealth)
                    {
                        healthComponent.CurrentHealth = healthComponent.MaximumHealth;
                    }
                    ComponentRepo.HealthComponents[keyComponent.Key] = healthComponent;
                }
            }
        }
    }
}