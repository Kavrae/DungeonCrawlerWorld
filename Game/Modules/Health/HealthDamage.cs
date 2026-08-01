using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.Health;

public static class HealthDamage
{
    public static void Apply(
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        int entityId,
        short amount,
        StatusEffectSource source,
        IPlayerQuery? playerQuery,
        string damageType)
    {
        if (!health.TryUpdate(entityId, amount, static (ref HealthComponent healthComponent, short damage) =>
            healthComponent.CurrentHealth = MathUtility.ClampShort((short)(healthComponent.CurrentHealth - damage), 0, healthComponent.MaximumHealth)))
        {
            return; // No HealthComponent -- fine, e.g. an "immortal" entity a status effect still applied to.
        }

        if (playerQuery is null)
        {
            return;
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId
            || (source.Kind == StatusEffectSourceKind.Entity && source.EntityId == playerQuery.PlayerEntityId);
        if (!playerInvolved)
        {
            return;
        }

        health.TryGetReadonly(entityId, out var updatedHealth);
        eventBus.Publish(new EntityDamaged(entityId, amount, source, updatedHealth.CurrentHealth, updatedHealth.MaximumHealth, damageType));
    }
}
