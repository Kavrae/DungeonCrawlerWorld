using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines an entity's health bounds and regeneration.
    /// This component should contain ONLY base health statistics. Additional health types should be separate components.
    /// </summary>
    public struct HealthComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public short CurrentHealth { get; set; }
        public short HealthRegen { get; set; }
        public short MaximumHealth { get; set; }

        public HealthComponent(int entityId) : this(entityId, 0, 0, 0) { }
        public HealthComponent(int entityId, short currentHealth, short healthRegen, short maximumHealth)
        {
            EntityId = entityId;
            CurrentHealth = currentHealth;
            HealthRegen = healthRegen;
            MaximumHealth = maximumHealth;

            ComponentRepo.HealthComponents[entityId] = this;
        }

        public override string ToString()
        {
            var barSize = 20;
            return StringUtility.BuildPercentageBar(CurrentHealth, MaximumHealth, barSize);
        }
    }
}