using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Actionable System
    /// Responsible for updating the energy values on an entity. These energy values allow the entity to perform various actions such as movement.
    /// </summary>
    public class EnergyRechargeSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 10;

        public EnergyRechargeSystem() { }

        public void Update(GameTime gameTime)
        {
            foreach (var keyComponent in ComponentRepo.EnergyComponents)
            {
                var actionEnergyComponent = keyComponent.Value;
                if (actionEnergyComponent.EnergyRecharge != 0)
                {
                    actionEnergyComponent.CurrentEnergy += actionEnergyComponent.EnergyRecharge;
                    if( actionEnergyComponent.CurrentEnergy > actionEnergyComponent.MaximumEnergy)
                    {
                        actionEnergyComponent.CurrentEnergy = actionEnergyComponent.MaximumEnergy;
                    }
                    ComponentRepo.EnergyComponents[keyComponent.Key] = actionEnergyComponent;
                }
            }
        }
    }
}