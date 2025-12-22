using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Classes
{
    public class Engineer : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.AddClassComponent(
                entityId,
                new ClassComponent(
                    new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d"),
                    "Engineer",
                    "TODO default engineer description"));

            //TODO temporary : engineers have 5% more energy and energy recharge
            if (ComponentRepo.EnergyComponents.TryGetValue(entityId, out var energyComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiple them here. Otherwise order matters.
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.05m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.05m);
                ComponentRepo.SaveEnergyComponent(entityId, energyComponent, ComponentSaveMode.Overwrite);
            }

            ComponentRepo.SaveDisplayTextComponent(entityId, new DisplayTextComponent
            {
                Name = "Engineer",
                Description = "TODO default engineer description"
            }, ComponentSaveMode.Merge);
        }
    }
}
