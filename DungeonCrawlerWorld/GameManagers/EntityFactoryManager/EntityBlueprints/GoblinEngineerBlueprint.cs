using System;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class GoblinEngineerBlueprint : IBaseFactoryTemplate
    {
        public override string Name => "Engineer";
        public override string Description => "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";
        public override void Build(Guid entityId)
        {
            var goblin = new Goblin();
            goblin.Build(entityId);

            var engineer = new Engineer();
            engineer.Apply(entityId);

            if (ComponentRepo.DisplayTextComponents.TryGetValue(entityId, out var displayTextComponent))
            {
                displayTextComponent.ClassName = Name;
                displayTextComponent.ClassDescription = Description;
                ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
            }

            //TODO temporary : goblin engineers have 10% more energy and energy recharge.
            if (ComponentRepo.EnergyComponents.TryGetValue(entityId, out var energyComponent))
            {
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
                ComponentRepo.EnergyComponents[entityId] = energyComponent;
            }
        }
    }
}
