using System;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class GoblinEngineerBlueprint : IBlueprint
    {

        public int EntityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        
        public GoblinEngineerBlueprint()
        {
            EntityId = ComponentRepo.GetNextEntityId();
            Name = "Engineer";
            Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";
            _ = new Goblin(EntityId);

            _ = new Engineer(EntityId);

            //TODO this is such a common pattern that I need to abstract it out to an "update or create" pattern
            var nullableDisplayTextComponent = ComponentRepo.DisplayTextComponents[EntityId];
            var displayTextComponent = nullableDisplayTextComponent ?? new DisplayTextComponent(EntityId);
            displayTextComponent.ClassName = Name;
            displayTextComponent.ClassDescription = Description;
            ComponentRepo.DisplayTextComponents[EntityId] = displayTextComponent;

            //TODO temporary : goblin engineers have 10% more energy and energy recharge on top of the engineering bonus
            if (ComponentRepo.EnergyComponents.TryGetValue(EntityId, out var energyComponent))
            {
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
                ComponentRepo.EnergyComponents[EntityId] = energyComponent;
            }
        }
    }
}
