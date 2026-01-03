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
            var healthComponentSet = ComponentRepo.HealthComponents;
            var entityIds = healthComponentSet.EntityIds;
            var components = healthComponentSet.Components;
            var count = healthComponentSet.Count;

            for (var healthIndex = 0; healthIndex < count; healthIndex++)
            {
                var entityId = entityIds[healthIndex];
                var healthComponent = components[healthIndex];

                if (healthComponent.HealthRegen != 0)
                {
                    healthComponent.CurrentHealth += healthComponent.HealthRegen;
                    healthComponent.CurrentHealth = MathUtility.ClampShort(healthComponent.CurrentHealth, 0, healthComponent.MaximumHealth);

                    healthComponentSet.Save(entityId, healthComponent);
                }
            }
        }
    }
}