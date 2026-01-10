using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;


namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Responsible for updating the health values on an entity via passive regeneration. 
    /// Health values are bound between a minimum (0) and maximum specified on the component.
    /// </summary>

    //TODO Health v2
    //  split health into multiple body parts, each with its own health
    //      Separate components or list of body part structs within health?
    //          Separate Components : 
    //              Need multi-storage like with race/class. Improve that framework.
    //              Slower iteration as we're pulling a lot more components just to update one of them.
    //              How would be pull a component for a specific body part easily?  Ex : damage feet from stepping on lava. Would need to pull all then check.
    //              Easy to add new ones or remove them. 
    //              Smaller individual components
    //          Single Component : 
    //              Keeps it as simple storage
    //              Faster retrieval
    //              Easier to figure out individual one to damage.
    //              Very large component
    //  Race determines body parts.
    //      How does merge work....?
    //  Health regen acts on the body part with the lowest percentage hit points.
    //  Body part at 0 health can't be used
    //  Each body part can have its own status effects like burning
    //      whole body burning = die a lot faster
    //  Each body part needs a designation for which type of part it is, for things like holding weapons, hats, body, etc
    //  Things without body parts just need a simple health bar, so maybe just a single body part, labelled to indicate that it's the whole entity's "body"?
    //  Overall hit points = X% of sum of body part hit poitns.  Ex : if you take 75% of total body part damage, you die. Shouldn't be 100%
    //  "required" body parts that, if they hit 0, you die regardless of other body parts. Ex : head and torso.

    public class HealthRegenSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 10;

        public HealthRegenSystem() { }

        public void Update(GameTime gameTime)
        {
            var healthComponentSet = ComponentRepo.HealthComponents;
            var entityIds = healthComponentSet.GetAllEntityIds();
            ref var components = ref healthComponentSet.GetAll();
            var count = healthComponentSet.Count;

            for (var healthIndex = 0; healthIndex < count; healthIndex++)
            {
                var entityId = entityIds[healthIndex];
                ref var healthComponent = ref components[healthIndex];

                if (healthComponent.HealthRegen != 0)
                {
                    healthComponent.CurrentHealth += healthComponent.HealthRegen;
                    healthComponent.CurrentHealth = MathUtility.ClampShort(healthComponent.CurrentHealth, 0, healthComponent.MaximumHealth);
                }
            }
        }
    }
}