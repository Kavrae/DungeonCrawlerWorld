using System.Threading.Tasks;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Responsible for updating the energy values on an entity. 
    /// These energy values allow the entity to perform various actions such as movement.
    /// Energy values are bound between a minimum (0) and maximum specified on the component.
    /// </summary>
    public class EnergyRechargeSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 10;

        public EnergyRechargeSystem() { }

        public void Update(GameTime gameTime)
        {
            Parallel.ForEach(ComponentRepo.EnergyComponents, keyValuePair =>
            {
                var actionEnergyComponent = keyValuePair.Value;
                if (actionEnergyComponent.EnergyRecharge != 0)
                {
                    actionEnergyComponent.CurrentEnergy += actionEnergyComponent.EnergyRecharge;
                    if (actionEnergyComponent.CurrentEnergy > actionEnergyComponent.MaximumEnergy)
                    {
                        actionEnergyComponent.CurrentEnergy = actionEnergyComponent.MaximumEnergy;
                    }

                    ComponentRepo.EnergyComponents[keyValuePair.Key] = actionEnergyComponent;
                }
            });
        }
    }
}