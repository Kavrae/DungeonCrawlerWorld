using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Health taken as a cost (HealthDrain): never reduced by damage reduction, split evenly across a body plan's parts, and asked first so it never kills.</summary>
/// <remarks>
/// A cost is not a hit: it publishes no EntityDamagedEvent and shows no floating text. Taken without asking
/// first (LeavesAlive), it can still kill, and then EntityDiedEvent is published as for damage. A body part a
/// share lands at 0 is disabled, as any damage to it would.
/// </remarks>
public static class HealthCost
{
    /// <summary>Whether entityId has health to pay with at all: a single pool or a body plan.</summary>
    public static bool HasHealth(PackedComponentPool<SimpleHealthComponent> health, EntityBodyParts bodyParts, int entityId) =>
        health.Has(entityId) || bodyParts.Has(entityId);

    /// <summary>Whether taking amount from entityId leaves it alive: more than amount left in a single pool, or more than its even share left in every Vital part.</summary>
    public static bool LeavesAlive(PackedComponentPool<SimpleHealthComponent> health, EntityBodyParts bodyParts, int entityId, float amount)
    {
        if (health.TryGetReadonly(entityId, out var simple))
        {
            return simple.CurrentHealth > amount;
        }

        var partCount = bodyParts.Count(entityId);
        if (partCount == 0)
        {
            return false;
        }

        var share = amount / partCount;
        foreach (var part in bodyParts.Parts(entityId))
        {
            if (part.IsVital && part.CurrentHealth <= share)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes amount from entityId, split evenly across its body parts if it has a body plan, with no damage reduction.</summary>
    /// <param name="now">The frame it is taken on -- a part landing at 0 is locked out of regen from it.</param>
    public static void Take(
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent> statModifiers,
        EventBus eventBus,
        IPlayerQuery playerQuery,
        PackedComponentPool<DeadComponent> deadEntities,
        int entityId,
        float amount,
        ActionSource source,
        long now)
    {
        if (amount <= 0 || deadEntities.Has(entityId))
        {
            return;
        }

        if (health.TryGetReadonly(entityId, out var before))
        {
            health.TryUpdate(entityId, amount, static (ref SimpleHealthComponent healthComponent, float amount) =>
                healthComponent.CurrentHealth = MathF.Max(0f, healthComponent.CurrentHealth - amount));

            if (before.CurrentHealth > 0 && health.GetReadonly(entityId).CurrentHealth == 0 && entityId != playerQuery.PlayerEntityId)
            {
                eventBus.Publish(new EntityDiedEvent(entityId, source));
            }

            return;
        }

        var partCount = bodyParts.Count(entityId);
        if (partCount == 0)
        {
            return;
        }

        var share = amount / partCount;
        for (var partId = 0; partId < partCount; partId++)
        {
            BodyPartDamageEffects.ApplyToPart(bodyParts, entityId, partId, statModifiers, share, now);
        }

        if (entityId == playerQuery.PlayerEntityId)
        {
            return;
        }

        foreach (var part in bodyParts.Parts(entityId))
        {
            if (part.IsVital && part.CurrentHealth == 0)
            {
                eventBus.Publish(new EntityDiedEvent(entityId, source));
                return;
            }
        }
    }
}
