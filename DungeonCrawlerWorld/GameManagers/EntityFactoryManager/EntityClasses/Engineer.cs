using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Engineer : ClassComponent
    {
        public override Guid ClassId => new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
        public Engineer(Guid entityId)
        {
            Name = "Engineer";
            Description = "TODO default engineer description";

            _ = new ClassGlyphComponent(entityId, "e", Color.OrangeRed, new Point(0, 0));

            //TODO temporary : engineers have 5% more energy and energy recharge
            if (ComponentRepo.EnergyComponents.TryGetValue(EntityId, out var energyComponent))
            {
                //TODO need to track additive and multiplicative bonuses, not just multiple them here. Otherwise order matters.
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.05m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.05m);
                ComponentRepo.EnergyComponents[EntityId] = energyComponent;
            }

            base.Build(entityId);
        }
    }
}
