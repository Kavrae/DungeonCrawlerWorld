using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>The single chokepoint for "what's this entity's current/max HP", regardless of whether it's Simple or Complex.</summary>
/// <remarks>
/// Mirrors IMapQuery.IsBlocking's own single-chokepoint reasoning, applied here to
/// Simple-vs-Complex. Deliberately does not fold in StatModifierMath's MaximumHealth modifier --
/// callers that need the modifier-effective maximum (InspectionWindowContent, MapWindow.DrawHealthBar)
/// apply StatModifierMath.GetEffectiveValue to the returned maximum themselves, same as they
/// already do today against SimpleHealthComponent.MaximumHealth directly; this only owns the
/// Simple-vs-Complex sum, not the modifier chain on top of it. See TryGetEffectiveMaximum below
/// for the combined, modifier-effective version.
/// </remarks>
public static class HealthQueries
{
    public static bool TryGetTotals(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        EntityBodyParts bodyParts,
        int entityId,
        out float current,
        out float maximum)
    {
        if (simpleHealth.TryGetReadonly(entityId, out var simple))
        {
            current = simple.CurrentHealth;
            maximum = simple.MaximumHealth;
            return true;
        }

        if (bodyParts.TryGetTotals(entityId, out current, out maximum))
        {
            return true;
        }

        current = 0f;
        maximum = 0f;
        return false;
    }

    /// <summary>TryGetTotals' own maximum, further scaled through StatModifierTarget.MaximumHealth -- the single place "this entity's real, modifier-effective max HP" is computed as one number regardless of Simple/Complex, used by both DirectDamage and DirectHeal's own percent-of-max-health calculations.</summary>
    public static bool TryGetEffectiveMaximum(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        EntityBodyParts bodyParts,
        MultiComponentPool<StatModifierComponent> statModifiers,
        int entityId,
        out float effectiveMaximum)
    {
        if (!TryGetTotals(simpleHealth, bodyParts, entityId, out _, out var maximum))
        {
            effectiveMaximum = 0f;
            return false;
        }

        effectiveMaximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, maximum);
        return true;
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
            return simple.CurrentHealth < StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, simple.MaximumHealth);
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

            if (part.CurrentHealth < StatModifierMath.CalculateTotal(part.MaximumHealth, additiveSum, multiplicativeSum))
            {
                return true;
            }
        }

        return false;
    }
}
