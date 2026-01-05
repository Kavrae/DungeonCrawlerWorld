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
            var energyComponentSet = ComponentRepo.EnergyComponents;
            var entityIds = energyComponentSet.AllEntityIds;
            ref var components = ref energyComponentSet.AllComponents;
            var count = energyComponentSet.Count;

            for (var energyIndex = 0; energyIndex < count; energyIndex++)
            {
                var entityId = entityIds[energyIndex];
                ref var energyComponent = ref components[energyIndex];

                if (energyComponent.EnergyRecharge != 0)
                {
                    energyComponent.CurrentEnergy += energyComponent.EnergyRecharge;
                    energyComponent.CurrentEnergy = MathUtility.ClampShort(energyComponent.CurrentEnergy, 0, energyComponent.MaximumEnergy);
                }
            }
        }
    }
}