using System.Threading.Tasks;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;


namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Responsible for updating the health values on an entity via passive regeneration. 
    /// Health values are bound between a minimum (0) and maximum specified on the component.
    /// </summary>
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
                    healthComponent.CurrentHealth = MathUtility.ClampShort( healthComponent.CurrentHealth, 0, healthComponent.MaximumHealth );
                    
                    ComponentRepo.HealthComponents[keyValuePair.Key] = healthComponent;
                }
            });
        }
    }
}