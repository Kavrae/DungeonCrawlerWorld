using System.Threading.Tasks;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Responsible for updating the energy values on an entity via passive regeneration.
    /// These energy values allow the entity to perform various actions such as movement.
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
                    actionEnergyComponent.CurrentEnergy = MathUtility.ClampShort(actionEnergyComponent.CurrentEnergy, 0, actionEnergyComponent.MaximumEnergy);

                    ComponentRepo.EnergyComponents[keyValuePair.Key] = actionEnergyComponent;
                }
            });
        }
    }
}