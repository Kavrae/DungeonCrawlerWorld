using System;
using System.Text;

namespace DungeonCrawlerWorld.Components
{
    public struct HealthComponent
    {
        public Guid EntityId { get; set; }
        public short CurrentHealth { get; set; }
        public short HealthRegen { get; set; }
        public short MaximumHealth { get; set; }

        public HealthComponent(Guid entityId) : this(entityId, 0, 0, 0) { }
        public HealthComponent(Guid entityId, short currentHealth, short healthRegen, short maximumHealth)
        {
            EntityId = entityId;
            CurrentHealth = currentHealth;
            HealthRegen = healthRegen;
            MaximumHealth = maximumHealth;

            ComponentRepo.HealthComponents.Remove(entityId);
            ComponentRepo.HealthComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            var healthOutOf20 = (int)(((float)CurrentHealth / MaximumHealth) * 20);
            var healthBarBuilder = new StringBuilder();
            healthBarBuilder.Append("HP : [");
            healthBarBuilder.Append('=', healthOutOf20);
            healthBarBuilder.Append('_', 20 - healthOutOf20);
            healthBarBuilder.Append(']');
            return healthBarBuilder.ToString();
        }
    }
}
