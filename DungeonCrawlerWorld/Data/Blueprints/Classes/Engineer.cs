using System;
using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Classes
{
    public class Engineer : IBlueprint
    {
        public static void Build(int entityId)
        {
            _ = new ClassComponent(entityId,
                new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d"),
                "Engineer",
                "TODO default engineer description\"");
            //TODO temporary : engineers have 5% more energy and energy recharge
            if (ComponentRepo.EnergyComponents.TryGetValue(entityId, out var energyComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiple them here. Otherwise order matters.
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.05m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.05m);
                ComponentRepo.EnergyComponents[entityId] = energyComponent;
            }
            var displayTextComponent = new DisplayTextComponent(entityId);
            displayTextComponent.Name += " Engineer"; //TODO format
            displayTextComponent.Description += $"{Environment.NewLine}Engineer class"; //TODO format
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}
