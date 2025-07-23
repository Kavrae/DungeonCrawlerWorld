using System;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class GoblinEngineerBlueprint : IBlueprint
    {

        public Guid EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        
        public GoblinEngineerBlueprint()
        {
            EntityId = Guid.NewGuid();
            Name = "Engineer";
            Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";
            _ = new Goblin(EntityId);

            _ = new Engineer(EntityId);

            if (ComponentRepo.DisplayTextComponents.TryGetValue(EntityId, out var displayTextComponent))
            {
                displayTextComponent.ClassName = Name;
                displayTextComponent.ClassDescription = Description;
                ComponentRepo.DisplayTextComponents[EntityId] = displayTextComponent;
            }

            //TODO temporary : goblin engineers have 10% more energy and energy recharge.
            if (ComponentRepo.EnergyComponents.TryGetValue(EntityId, out var energyComponent))
            {
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
                ComponentRepo.EnergyComponents[EntityId] = energyComponent;
            }
        }
    }
}
