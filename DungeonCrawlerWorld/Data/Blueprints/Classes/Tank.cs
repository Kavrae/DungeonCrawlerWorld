using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Classes
{
    public class Tank : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.ClassComponents.Add(
                entityId,
                new ClassComponent(
                    new("45ddf671-3f76-4e23-9ac3-7a588282ec35"),
                    "Tank",
                    "Extra hit points"));

            //TODO temporary : tanks have 10% more health and health regen
            if (ComponentRepo.HealthComponents.HasComponent(entityId))
            {
                ref var healthComponent = ref ComponentRepo.HealthComponents.Get(entityId);
                healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
                healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
            }

            ComponentRepo.DisplayTextComponents.Add(entityId, new DisplayTextComponent
            {
                Name = "Tank",
                Description = "Tank class"
            });
        }
    }
}
