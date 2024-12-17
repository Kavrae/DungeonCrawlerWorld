using System;

namespace DungeonCrawlerWorld.Components
{
    public struct EnergyComponent
    {
        public Guid EntityId { get; set; }
        public short CurrentEnergy { get; set; }
        public short EnergyRecharge { get; set; }
        public short MaximumEnergy { get; set; }

        public EnergyComponent(Guid entityId) : this(entityId, 0, 0, 0) { }
        public EnergyComponent(Guid entityId, short currentEnergy, short energyRecharge, short maximumEnergy)
        {
            EntityId = entityId;
            CurrentEnergy = currentEnergy;
            EnergyRecharge = energyRecharge;
            MaximumEnergy = maximumEnergy;

            ComponentRepo.ActionEnergyComponents.Remove(entityId);
            ComponentRepo.ActionEnergyComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return $"Energy : {CurrentEnergy} / {MaximumEnergy}";
        }
    }
}
