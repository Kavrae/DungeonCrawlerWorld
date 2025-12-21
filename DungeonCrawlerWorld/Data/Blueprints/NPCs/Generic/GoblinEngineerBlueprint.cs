using System;
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

            //TODO this is such a common pattern that I need to abstract it out to an "update or create" pattern
            var nullableDisplayTextComponent = ComponentRepo.DisplayTextComponents[entityId];
            var displayTextComponent = nullableDisplayTextComponent ?? new DisplayTextComponent(entityId);
            displayTextComponent.Name = "Goblin Engineer";
            displayTextComponent.Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;

            //TODO temporary : goblin engineers have 10% more energy and energy recharge on top of the engineering bonus
            if (ComponentRepo.EnergyComponents.TryGetValue(entityId, out var energyComponent))
            {
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
                ComponentRepo.EnergyComponents[entityId] = energyComponent;
            }
        }
    }
}
