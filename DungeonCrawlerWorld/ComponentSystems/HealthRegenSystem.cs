using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;


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
            int entityId;
            HealthComponent healthComponent;

            foreach (var keyValuePair in ComponentRepo.HealthComponents)
            {
                entityId = keyValuePair.Key;
                healthComponent = keyValuePair.Value;

                if (healthComponent.HealthRegen != 0)
                {
                    healthComponent.CurrentHealth += healthComponent.HealthRegen;
                    healthComponent.CurrentHealth = MathUtility.ClampShort(healthComponent.CurrentHealth, 0, healthComponent.MaximumHealth);

                    ComponentRepo.SaveHealthComponent(keyValuePair.Key, healthComponent, ComponentSaveMode.Overwrite);
                }
            }
        }
    }
}