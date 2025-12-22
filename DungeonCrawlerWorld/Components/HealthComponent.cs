using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines an entity's health bounds and regeneration.
    /// This component should contain ONLY base health statistics. Additional health types should be separate components.
    /// </summary>
    public struct HealthComponent(short currentHealth, short healthRegen, short maximumHealth) : IEntityComponent
    {
        public short CurrentHealth { get; set; } = currentHealth;
        public short HealthRegen { get; set; } = healthRegen;
        public short MaximumHealth { get; set; } = maximumHealth;

        public override string ToString()
        {
            var barSize = 20;
            return StringUtility.BuildPercentageBar(CurrentHealth, MaximumHealth, barSize);
        }
    }
}