using System;
using System.Text;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines an entity's energy bounds and regeneration.
    /// This component should contain ONLY base energy statistics. Additional energy types should be separate components.
    /// Energy is utilized by other systems as a method of restricting an entity's speed and action count.
    /// Ex : an entity that attempts to run by moving twice as much is unlikely to have enough energy to also attack.
    /// </summary>
    public struct EnergyComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public short CurrentEnergy { get; set; }
        public short EnergyRecharge { get; set; }
        public short MaximumEnergy { get; set; }

        public EnergyComponent(int entityId) : this(entityId, 0, 0, 0) { }
        public EnergyComponent(int entityId, short currentEnergy, short energyRecharge, short maximumEnergy)
        {
            EntityId = entityId;
            CurrentEnergy = currentEnergy;
            EnergyRecharge = energyRecharge;
            MaximumEnergy = maximumEnergy;

            ComponentRepo.EnergyComponents[entityId] = this;
        }

        public override string ToString()
        {
            var manaOutOf20 = (int)(((float)CurrentEnergy / MaximumEnergy) * 20);
            var manaBarBuilder = new StringBuilder();
            manaBarBuilder.Append("Mana : [");
            manaBarBuilder.Append('=', manaOutOf20);
            manaBarBuilder.Append('_', 20 - manaOutOf20);
            manaBarBuilder.Append(']');
            return manaBarBuilder.ToString();
        }
    }
}
