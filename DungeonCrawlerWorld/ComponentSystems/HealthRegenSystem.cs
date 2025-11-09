using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using System.Threading.Tasks;

namespace DungeonCrawlerWorld.ComponentSystems
{
    public class HealthRegenSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 10;

        public HealthRegenSystem() { }

        public void Update(GameTime gameTime)
        {
            Parallel.ForEach(ComponentRepo.HealthComponents, keyValuePair =>
            {
                var healthComponent = keyValuePair.Value;
                if (healthComponent.HealthRegen != 0)
                {
                    healthComponent.CurrentHealth += healthComponent.HealthRegen;
                    if (healthComponent.CurrentHealth > healthComponent.MaximumHealth)
                    {
                        healthComponent.CurrentHealth = healthComponent.MaximumHealth;
                    }
                    
                    ComponentRepo.HealthComponents[keyValuePair.Key] = healthComponent;
                }
            });
        }
    }
}