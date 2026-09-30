using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Tags;
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
/// Publishes EntityHealedEvent (mirroring HealthDamage.Apply's EntityDamagedEvent) only when the
/// player is involved as either source or target.
///
/// `now` is the simulation frame this heal lands on. Only the Complex single-part path consults it,
/// to skip a body part still inside its regen lockout (BodyPartComponent.RegenLockedUntilFrame);
/// required rather than optional for the same reason HealthDamage.Apply's is.
/// </remarks>
public static class HealthHeal
{
    public static void Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        long now,
        MultiComponentPool<StatModifierComponent> statModifiers,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        IPlayerQuery playerQuery,
        FloatingTextFeed floatingTextFeed,
        HealCategory healCategory,
        float flatAmount = 0f,
        int? sourceEntityId = null,
        GameplayTagSet activatorTags = default,
        BodyPartTargetMode targetMode = BodyPartTargetMode.All,
        BodyPartTargetRule? targetRule = null,
        MathUtility? mathUtility = null,
        string healType = "Heal")
    {
        var healthBefore = 0f;
        var showsHealedText = floatingTextFeed.IsShownFor(entityId)
            && HealthQueries.TryGetTotals(health, bodyParts, entityId, out healthBefore, out _);

        ApplyHeal(health, entityId, percentOfMaxHealth, now, statModifiers, bodyParts, eventBus, playerQuery, flatAmount, sourceEntityId, activatorTags, targetMode, targetRule, mathUtility, healType);

        if (showsHealedText && HealthQueries.TryGetTotals(health, bodyParts, entityId, out var healthAfter, out _))
        {
            PublishHealed(floatingTextFeed, entityId, healthBefore, healthAfter, healCategory);
        }
    }

    /// <summary>Publishes the floating text for the health entityId gained, as the HUD displays it.</summary>
    /// <remarks>The HUD rounds current health up, so the amount is the change in the rounded-up value: what the player sees the bar number move by, never a "+0" for a fraction of a point. Regeneration gains a fraction of a point per visit, so its text appears only on the visits that move the displayed value.</remarks>
    public static void PublishHealed(FloatingTextFeed floatingTextFeed, int entityId, float healthBefore, float healthAfter, HealCategory healCategory)
    {
        var displayedGain = (int)MathF.Ceiling(healthAfter) - (int)MathF.Ceiling(healthBefore);
        if (displayedGain <= 0)
        {
            return;
        }

        var kind = healCategory == HealCategory.Regeneration ? FloatingTextKind.Regenerated : FloatingTextKind.Healed;
        floatingTextFeed.Publish(entityId, kind, (ushort)System.Math.Min(displayedGain, ushort.MaxValue));
    }

    private static void ApplyHeal(
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        long now,
        MultiComponentPool<StatModifierComponent> statModifiers,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        IPlayerQuery playerQuery,
        float flatAmount,
        int? sourceEntityId,
        GameplayTagSet activatorTags,
        BodyPartTargetMode targetMode,
        BodyPartTargetRule? targetRule,
        MathUtility? mathUtility,
        string healType)
    {
        if (!health.Has(entityId))
        {
            if (bodyParts.Has(entityId))
            {
                if (targetMode == BodyPartTargetMode.All)
                {
                    ComplexHealthHeal.ApplyToAllParts(bodyParts, health, entityId, percentOfMaxHealth, flatAmount, statModifiers, eventBus, playerQuery, sourceEntityId, activatorTags, healType);
                }
                else
                {
                    ComplexHealthHeal.ApplyToSinglePart(bodyParts, health, entityId, percentOfMaxHealth, flatAmount, statModifiers, sourceEntityId, activatorTags, targetRule, targetMode, mathUtility, now, eventBus, playerQuery, healType);
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
    internal static float ComputeAmount(MultiComponentPool<StatModifierComponent> statModifiers, int? sourceEntityId, int targetEntityId, GameplayTagSet activatorTags, float percentOfMaxHealth, float flatAmount, float effectiveMaxHealth)
    {
        var amount = flatAmount + percentOfMaxHealth * effectiveMaxHealth;

        if (sourceEntityId is { } source)
        {
            amount = StatModifierMath.GetEffectiveValue(statModifiers, source, StatModifierTarget.OutgoingHealing, amount, activatorTags);
        }

        return StatModifierMath.GetEffectiveValue(statModifiers, targetEntityId, StatModifierTarget.IncomingHealing, amount, activatorTags);
    }

    /// <summary>Shared by the Simple path here and every ComplexHealthHeal path -- publishes EntityHealedEvent only when the player is involved as either entityId or sourceEntityId, mirroring HealthDamage.Apply's identical playerInvolved gate.</summary>
    internal static void PublishHealEvent(EventBus eventBus, IPlayerQuery playerQuery, int entityId, int? sourceEntityId, float amount, string healType, float currentHealth, float maximumHealth)
    {
        var playerInvolved = entityId == playerQuery.PlayerEntityId || sourceEntityId == playerQuery.PlayerEntityId;
        if (!playerInvolved)
        {
            return;
        }

        eventBus.Publish(new EntityHealedEvent(entityId, amount, sourceEntityId, currentHealth, maximumHealth, healType));
    }
}
