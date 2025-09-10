using System;
using System.Text;

namespace DungeonCrawlerWorld.Components
{
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
