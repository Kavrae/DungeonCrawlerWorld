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
        MultiComponentPool<BodyPartComponent>? bodyParts,
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

        if (bodyParts?.Has(entityId) != true)
        {
            current = 0f;
            maximum = 0f;
            return false;
        }

        current = 0f;
        maximum = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            current += part.CurrentHealth;
            maximum += part.MaximumHealth;
        }

        return true;
    }

    /// <summary>TryGetTotals' own maximum, further scaled through StatModifierTarget.MaximumHealth -- the single place "this entity's real, modifier-effective max HP" is computed as one number regardless of Simple/Complex, used by both DirectDamage and DirectHeal's own percent-of-max-health calculations.</summary>
    public static bool TryGetEffectiveMaximum(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        MultiComponentPool<BodyPartComponent>? bodyParts,
        MultiComponentPool<StatModifierComponent>? statModifiers,
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
}
