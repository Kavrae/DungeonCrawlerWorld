using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

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
        string damageType,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        if (!health.TryGetReadonly(entityId, out var beforeHealth))
        {
            return; // No HealthComponent -- fine, e.g. an "immortal" entity a status effect still applied to.
        }

        var wasAlive = beforeHealth.CurrentHealth > 0;

        // Reduced by the target's own IncomingDamage modifiers (e.g. a flat damage-reduction
        // buff) before anything else -- clamped at 0 so a large enough reduction can't turn
        // damage into healing. Computed once up front (not per-call-site) since both the health
        // clamp below and the EntityDamagedEvent need the same, already-reduced amount.
        var effectiveAmount = MathUtility.ClampShort(
            (short)StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount),
            0,
            short.MaxValue);

        // Clamped against the effective (modifier-adjusted) max, not the raw stored field, so a
        // permanent +max-HP buff actually raises the ceiling damage is clamped against -- see
        // StatModifierMath's own doc comment for why this is recomputed here rather than baked
        // into HealthComponent.MaximumHealth itself.
        health.TryUpdate(entityId, (statModifiers, entityId, effectiveAmount), static (ref HealthComponent healthComponent, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, short Amount) state) =>
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, healthComponent.MaximumHealth);
            healthComponent.CurrentHealth = MathHelper.Clamp(healthComponent.CurrentHealth - state.Amount, 0f, effectiveMaximumHealth);
        });

        health.TryGetReadonly(entityId, out var updatedHealth);

        // Only on the wasAlive -> 0 transition, not every subsequent hit against an
        // already-dead corpse -- and never for the player, who is deliberately exempted from
        // dying for now (see TODO.md's Death at 0 HP item: the player-specific reaction, a game
        // over screen, doesn't exist yet). Published unconditionally otherwise (unlike
        // EntityDamagedEvent below, which only fires when the player is involved) since death needs
        // to be knowable for any entity, not just player-involved damage.
        if (wasAlive && updatedHealth.CurrentHealth == 0 && entityId != playerQuery?.PlayerEntityId)
        {
            eventBus.Publish(new EntityDiedEvent(entityId, source));
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

        // EntityDamagedEvent's Current/MaximumHealth are short -- it's a display/logging event (see
        // its own doc comment), not simulation state, so it truncates the same way HealthComponent.
        // ToString() does rather than widening its contract to float for a fractional value
        // nothing reading this event needs.
        var effectiveMaximumHealthForEvent = (short)StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, updatedHealth.MaximumHealth);
        eventBus.Publish(new EntityDamagedEvent(entityId, effectiveAmount, source, (short)updatedHealth.CurrentHealth, effectiveMaximumHealthForEvent, damageType));
    }
}
