using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>The single chokepoint for "what's this entity's current/max HP", regardless of whether it's Simple or Complex.</summary>
/// <remarks>
/// Every maximum it hands out has the entity's MaximumHealth modifiers applied, because current health already has them
/// (MaximumHealthShift moves it with them): a current health compared with a base maximum reads a Boss as 160% healthy.
/// The base maximum is read only where it is the point -- MaximumHealthShift, the stored components -- under its own
/// name (BaseMaximumHealth, EntityBodyParts.TryGetCurrentAndBaseMaximumTotals).
/// </remarks>
public static class HealthQueries
{
    /// <summary>Its current health and its maximum with its MaximumHealth modifiers applied; false for an entity with no health.</summary>
    /// <remarks>For Complex health the modifiers apply to each part and the parts are summed, as each part's own maximum is read everywhere else: an additive modifier adds to every part.</remarks>
    public static bool TryGetTotals(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent> statModifiers,
        int entityId,
        out float current,
        out float maximum)
    {
        if (simpleHealth.TryGetReadonly(entityId, out var simple))
        {
            current = simple.CurrentHealth;
            maximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, simple.BaseMaximumHealth);
            return true;
        }

        current = 0f;
        maximum = 0f;
        var any = false;
        var additiveSum = 0f;
        var multiplicativeSum = 0f;
        foreach (var part in bodyParts.Parts(entityId))
        {
            if (!any)
            {
                StatModifierMath.GetSums(statModifiers, entityId, StatModifierTarget.MaximumHealth, out additiveSum, out multiplicativeSum);
                any = true;
            }

            current += part.CurrentHealth;
            maximum += StatModifierMath.CalculateTotal(part.BaseMaximumHealth, additiveSum, multiplicativeSum);
        }

        return any;
    }

    /// <summary>Its maximum health with its MaximumHealth modifiers applied -- what DirectDamage's and DirectHeal's percent-of-maximum amounts read.</summary>
    public static bool TryGetEffectiveMaximum(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent> statModifiers,
        int entityId,
        out float effectiveMaximum) =>
        TryGetTotals(simpleHealth, bodyParts, statModifiers, entityId, out _, out effectiveMaximum);

    /// <summary>Its maximum health before any MaximumHealth modifier, summed across its parts for Complex health; false for an entity with no health.</summary>
    /// <remarks>For what changes the base itself (a race applied or changed while modifiers are active), never for comparing with current health. A part's own base is BodyPartView.BaseMaximumHealth.</remarks>
    public static bool TryGetBaseMaximum(PackedComponentPool<SimpleHealthComponent> simpleHealth, EntityBodyParts bodyParts, int entityId, out float baseMaximum) =>
        TryGetCurrentAndBaseMaximum(simpleHealth, bodyParts, entityId, out _, out baseMaximum);

    private static bool TryGetCurrentAndBaseMaximum(PackedComponentPool<SimpleHealthComponent> simpleHealth, EntityBodyParts bodyParts, int entityId, out float current, out float baseMaximum)
    {
        if (simpleHealth.TryGetReadonly(entityId, out var simple))
        {
            current = simple.CurrentHealth;
            baseMaximum = simple.BaseMaximumHealth;
            return true;
        }

        return bodyParts.TryGetCurrentAndBaseMaximumTotals(entityId, out current, out baseMaximum);
    }

    /// <summary>Whether entityId has any health a heal could restore: below its modifier-effective maximum for Simple health, or any one body part below its own for Complex.</summary>
    /// <remarks>
    /// The one gate every heal passes first (HealthHeal.Apply), so a heal of something already at full
    /// health costs one walk of its modifiers whichever shape its health has. The entity's
    /// MaximumHealth modifiers are summed once and applied to each part. False for an entity with no
    /// health at all. Says nothing about whether a particular heal can reach the missing health: a
    /// part locked out of regeneration still counts here, and BodyPartSelection decides that.
    /// </remarks>
    public static bool HasMissingHealth(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent> statModifiers,
        int entityId)
    {
        if (simpleHealth.TryGetReadonly(entityId, out var simple))
        {
            return simple.CurrentHealth < StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, simple.BaseMaximumHealth);
        }

        var modifiersSummed = false;
        var additiveSum = 0f;
        var multiplicativeSum = 0f;

        foreach (var part in bodyParts.Parts(entityId))
        {
            if (!modifiersSummed)
            {
                StatModifierMath.GetSums(statModifiers, entityId, StatModifierTarget.MaximumHealth, out additiveSum, out multiplicativeSum);
                modifiersSummed = true;
            }

            if (part.CurrentHealth < StatModifierMath.CalculateTotal(part.BaseMaximumHealth, additiveSum, multiplicativeSum))
            {
                return true;
            }
        }

        return false;
    }
}
