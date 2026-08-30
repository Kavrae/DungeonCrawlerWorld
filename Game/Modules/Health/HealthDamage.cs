using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Simple/Complex dispatching facade for dealing damage -- the one shared chokepoint every non-ActionEffect damage caller (ContactDamageSystem, PoisonSystem/BurningSystem's DoT ticks) as well as ActionEffect's own DirectDamage lean on.</summary>
/// <remarks>
/// Dispatches on which pool actually has entityId: SimpleHealthComponent runs the original
/// single-pool logic unchanged; a BodyPartComponent-owning entity with no SimpleHealthComponent
/// delegates to ComplexHealthDamage.Apply, which requires mathUtility -- a caller that reaches a
/// Complex entity without ever wiring mathUtility is a real construction bug, not a state worth
/// degrading gracefully from (mirrors MovementModule.Configure's own still-null-dependency
/// throw). Neither pool having entityId is today's existing no-op -- an "immortal" entity a
/// status effect still applied to.
/// </remarks>
public static class HealthDamage
{
    public static void Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        int entityId,
        ushort amount,
        StatusEffectSource source,
        IPlayerQuery? playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null,
        MathUtility? mathUtility = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        BodyPartTargetRule? targetRule = null,
        IReadOnlyList<Tag>? damageTags = null,
        BodyPartTargetMode targetMode = BodyPartTargetMode.SingleTarget)
    {
        if (!health.TryGetReadonly(entityId, out var beforeHealth))
        {
            if (bodyParts?.Has(entityId) == true)
            {
                if (mathUtility is null)
                {
                    throw new InvalidOperationException($"{nameof(HealthDamage)}.{nameof(Apply)} requires {nameof(mathUtility)} to be set for a Complex-health entity (entityId {entityId}).");
                }

                if (targetMode == BodyPartTargetMode.All)
                {
                    ComplexHealthDamage.ApplyToAllParts(health, bodyParts, eventBus, entityId, amount, source, playerQuery, damageType, statModifiers, deadEntities, damageTags);
                }
                else
                {
                    ComplexHealthDamage.Apply(health, bodyParts, eventBus, entityId, amount, source, playerQuery, damageType, statModifiers, mathUtility, deadEntities, targetRule, damageTags, targetMode);
                }
            }

            return; // No SimpleHealthComponent or BodyPartComponent -- fine, e.g. an "immortal" entity a status effect still applied to.
        }

        var wasAlive = beforeHealth.CurrentHealth > 0;

        // Reduced by the target's own IncomingDamage modifiers (e.g. a flat damage-reduction
        // buff) before anything else -- clamped at 0 so a large enough reduction can't turn
        // damage into healing. Computed once up front (not per-call-site) since both the health
        // clamp below and the EntityDamagedEvent need the same, already-reduced amount.
        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount, damageTags),
            0,
            ushort.MaxValue);

        // Clamped against the effective (modifier-adjusted) max, not the raw stored field, so a
        // permanent +max-HP buff actually raises the ceiling damage is clamped against -- see
        // StatModifierMath's own doc comment for why this is recomputed here rather than baked
        // into SimpleHealthComponent.MaximumHealth itself.
        health.TryUpdate(entityId, (statModifiers, entityId, effectiveAmount), static (ref SimpleHealthComponent healthComponent, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, ushort Amount) state) =>
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
        // its own doc comment), not simulation state, so it truncates the same way SimpleHealthComponent.
        // ToString() does rather than widening its contract to float for a fractional value
        // nothing reading this event needs.
        var effectiveMaximumHealthForEvent = MathUtility.ClampUShort(StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, updatedHealth.MaximumHealth), 0, ushort.MaxValue);
        eventBus.Publish(new EntityDamagedEvent(entityId, effectiveAmount, source, (ushort)updatedHealth.CurrentHealth, effectiveMaximumHealthForEvent, damageType));
    }
}
