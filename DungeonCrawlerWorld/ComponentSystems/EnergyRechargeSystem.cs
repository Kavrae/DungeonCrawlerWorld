using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;

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
            int entityId;
            EnergyComponent energyComponent;

            foreach (var keyValuePair in ComponentRepo.EnergyComponents)
            {
                entityId = keyValuePair.Key;
                energyComponent = keyValuePair.Value;

                if (energyComponent.EnergyRecharge != 0)
                {
                    energyComponent.CurrentEnergy += energyComponent.EnergyRecharge;
                    energyComponent.CurrentEnergy = MathUtility.ClampShort(energyComponent.CurrentEnergy, 0, energyComponent.MaximumEnergy);

                    ComponentRepo.SaveEnergyComponent(keyValuePair.Key, energyComponent, ComponentSaveMode.Overwrite);
                }
            }
        }
    }
}