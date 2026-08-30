using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Simple/Complex dispatching facade for healing -- the shared chokepoint DirectHeal, and every regen system, lean on.</summary>
/// <remarks>
/// Dispatches on which pool actually has entityId, mirroring HealthDamage.Apply. FlatAmount and
/// percentOfMaxHealth (of the modifier-effective MaximumHealth, HealthQueries.TryGetEffectiveMaximum)
/// combine into one base amount (ComputeAmount) before OutgoingHealing (sourceEntityId's own
/// modifiers, when known) and then IncomingHealing (entityId's own modifiers) scale it -- the same
/// tag-conditional-via-activatorTags shape DirectDamage's OutgoingDamage/HealthDamage's
/// IncomingDamage already use. SimpleHealthRegenSystem/ComplexHealthRegenSystem route their own
/// periodic ticks through here too (sourceEntityId: entityId, a self-heal), which is exactly what
/// lets a HealthRegen-scaled regen tick also carry Outgoing/IncomingHealing modifiers -- something
/// no regen tick could do before this existed. A SimpleHealthComponent applies the single computed
/// amount directly, clamped against the effective maximum rather than the raw stored field (see
/// StatModifierMath's own doc comment for why). A BodyPartComponent-owning entity with no
/// SimpleHealthComponent delegates to ComplexHealthHeal (targetMode All vs a single part -- see
/// its own doc comment). Neither pool having entityId is a no-op, same as HealthDamage.Apply.
/// Publishes EntityHealedEvent (mirroring HealthDamage.Apply's EntityDamagedEvent) only when both
/// eventBus and playerQuery are supplied and the player is involved as either source or target --
/// both are optional here (unlike HealthDamage.Apply's required eventBus) since most existing
/// low-level callers/tests have no need to observe a heal landing.
/// </remarks>
public static class HealthHeal
{
    public static void Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null,
        float flatAmount = 0f,
        int? sourceEntityId = null,
        IReadOnlyList<Tag>? activatorTags = null,
        BodyPartTargetMode targetMode = BodyPartTargetMode.All,
        BodyPartTargetRule? targetRule = null,
        MathUtility? mathUtility = null,
        MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        string healType = "Heal")
    {
        if (!health.Has(entityId))
        {
            if (bodyParts?.Has(entityId) == true)
            {
                if (targetMode == BodyPartTargetMode.All)
                {
                    ComplexHealthHeal.ApplyToAllParts(bodyParts, health, entityId, percentOfMaxHealth, flatAmount, statModifiers, sourceEntityId, activatorTags, eventBus, playerQuery, healType);
                }
                else
                {
                    ComplexHealthHeal.ApplyToSinglePart(bodyParts, health, entityId, percentOfMaxHealth, flatAmount, statModifiers, sourceEntityId, activatorTags, targetRule, targetMode, mathUtility, bodyPartBurningTimers, eventBus, playerQuery, healType);
                }
            }

            return;
        }

        if (!HealthQueries.TryGetEffectiveMaximum(health, bodyParts, statModifiers, entityId, out var effectiveMaximumHealth))
        {
            return;
        }

        var amount = ComputeAmount(statModifiers, sourceEntityId, entityId, activatorTags, percentOfMaxHealth, flatAmount, effectiveMaximumHealth);

        health.TryUpdate(entityId, (amount, effectiveMaximumHealth), static (ref SimpleHealthComponent healthComponent, (float Amount, float EffectiveMaximumHealth) state) =>
        {
            healthComponent.CurrentHealth = MathHelper.Clamp(healthComponent.CurrentHealth + state.Amount, 0f, state.EffectiveMaximumHealth);
        });

        PublishHealEvent(eventBus, playerQuery, entityId, sourceEntityId, amount, healType, health.GetReadonly(entityId).CurrentHealth, effectiveMaximumHealth);
    }

    /// <summary>flat + percent*effectiveMaxHealth, then OutgoingHealing (sourceEntityId, if known) then IncomingHealing (targetEntityId) -- shared by the Simple path above and every ComplexHealthHeal path, so a body-parts entity gets the exact same modifier chain as a Simple one.</summary>
    internal static float ComputeAmount(MultiComponentPool<StatModifierComponent>? statModifiers, int? sourceEntityId, int targetEntityId, IReadOnlyList<Tag>? activatorTags, float percentOfMaxHealth, float flatAmount, float effectiveMaxHealth)
    {
        var amount = flatAmount + percentOfMaxHealth * effectiveMaxHealth;

        if (sourceEntityId is { } source)
        {
            amount = StatModifierMath.GetEffectiveValue(statModifiers, source, StatModifierTarget.OutgoingHealing, amount, activatorTags);
        }

        return StatModifierMath.GetEffectiveValue(statModifiers, targetEntityId, StatModifierTarget.IncomingHealing, amount, activatorTags);
    }

    /// <summary>Shared by the Simple path here and every ComplexHealthHeal path -- publishes EntityHealedEvent only when both eventBus and playerQuery are wired in and the player is involved as either entityId or sourceEntityId, mirroring HealthDamage.Apply's identical playerInvolved gate.</summary>
    internal static void PublishHealEvent(EventBus? eventBus, IPlayerQuery? playerQuery, int entityId, int? sourceEntityId, float amount, string healType, float currentHealth, float maximumHealth)
    {
        if (eventBus is null || playerQuery is null)
        {
            return;
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId || sourceEntityId == playerQuery.PlayerEntityId;
        if (!playerInvolved)
        {
            return;
        }

        var source = sourceEntityId is { } s ? StatusEffectSource.FromEntity(s) : StatusEffectSource.Admin;
        eventBus.Publish(new EntityHealedEvent(entityId, amount, source, currentHealth, maximumHealth, healType));
    }
}
