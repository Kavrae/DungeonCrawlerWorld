using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Classes
{
    public class Tank : IBlueprint
    {
        public static void Build(int entityId)
        {
            _ = new ClassComponent(entityId,
                new("45ddf671-3f76-4e23-9ac3-7a588282ec35"),
                "Tank",
                "Extra hit points");

            //TODO temporary : tanks have 10% more health and health regen
            if (ComponentRepo.HealthComponents.TryGetValue(entityId, out var healthComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiply them here. Otherwise order matters and introduces many bugs.
                healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
                healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
                ComponentRepo.HealthComponents[entityId] = healthComponent;
            }

            var displayTextComponent = new DisplayTextComponent(entityId);
            displayTextComponent.Name += " Tank"; //TODO format
            displayTextComponent.Description += " Tank class"; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
