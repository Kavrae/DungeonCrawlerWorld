using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data.Blueprints.Classes;
using DungeonCrawlerWorld.Data.Blueprints.Races;

namespace DungeonCrawlerWorld.Data.Blueprints.Npcs
{
    /// <summary>
    /// Represents a goblin engineer NPC as a combination of the Goblin race and Engineer class.
    /// This combination grants an additional bonus to energy and energy regeneration.
    /// </summary>
    public class GoblinEngineerBlueprint : IBlueprint
    {
        public static void Build(int entityId)
        {
            Goblin.Build(entityId);
            Engineer.Build(entityId);

            ComponentRepo.DisplayTextComponents.Add(entityId, new DisplayTextComponent
            {
                Name = "Goblin Engineer",
                Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first."
            });

            if (ComponentRepo.EnergyComponents.HasComponent(entityId))
            {
                ref var energyComponent = ref ComponentRepo.EnergyComponents.Get(entityId);
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
            }
        }
    }
}
