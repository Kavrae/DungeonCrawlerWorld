using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>Selection rules for picking one of a Complex entity's BodyPartComponent instances.</summary>
/// <remarks>Shared by every system/helper that needs to pick a part, rather than each re-walking entityId's chain its own way.</remarks>
public static class BodyPartSelection
{
    /// <summary>Picks one of entityId's body parts uniformly at random.</summary>
    /// <remarks>
    /// The "attacks hit a random body part (for now)" placeholder TODO.md's Body parts item names,
    /// until the Targeted body part damage follow-up adds real selection rules. Two-pass walk
    /// (count, then walk to the Nth) since MultiComponentPool exposes no direct "the Nth instance
    /// for this entity" accessor. Returns -1 if entityId owns no BodyPartComponent at all.
    /// </remarks>
    public static int PickRandom(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MathUtility mathUtility)
    {
        var count = bodyParts.CountForEntity(entityId);
        if (count == 0)
        {
            return -1;
        }

        var targetOrdinal = mathUtility.Next(0, count);
        var ordinal = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex), ordinal++)
        {
            if (ordinal == targetOrdinal)
            {
                return denseIndex;
            }
        }

        return -1; // Unreachable given count > 0, guarded for completeness.
    }

    /// <summary>Picks entityId's body part with the lowest CurrentHealth/effective-MaximumHealth fraction, skipping any part still inside its post-disable lockout window.</summary>
    /// <remarks>
    /// The yo-yo-prevention case RegenLockoutFramesRemaining exists for. Its only caller is
    /// ComplexHealthRegenSystem's own passive-regen tick -- an active heal (potion/scroll) never
    /// goes through this method at all, see ComplexHealthHeal.ApplyFractionToAllParts, which heals
    /// every part at once rather than picking one, so there is no "should this ignore the lockout"
    /// question for the heal path to begin with. Fraction is computed against each part's own
    /// modifier-effective maximum (StatModifierMath, same chain ComplexHealthDamage/
    /// ComplexHealthRegenSystem's own clamp already uses), not the raw MaximumHealth field -- a
    /// part sitting at 100% of its raw max with an active MaximumHealth buff still has real
    /// headroom up to the effective one, and treating it as "already full" here would leave it
    /// permanently unselectable, stuck below the true cap regen should still be closing. Returns
    /// -1 if entityId owns no BodyPartComponent, or every part is either at its effective maximum
    /// or currently locked out.
    /// </remarks>
    public static int PickLowestPercentage(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        var bestDenseIndex = -1;
        var bestFraction = float.MaxValue;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.RegenLockoutFramesRemaining > 0)
            {
                continue;
            }

            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            var fraction = effectiveMaximumHealth > 0 ? part.CurrentHealth / effectiveMaximumHealth : 1f;
            if (fraction >= 1f)
            {
                continue; // Already at its effective maximum, nothing to gain by selecting it.
            }

            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }

    /// <summary>Picks entityId's highest-VerticalPosition body part (e.g. the Head).</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent.</remarks>
    public static int PickTopmost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId) =>
        PickExtreme(bodyParts, entityId, preferHigher: true);

    /// <summary>Picks entityId's lowest-VerticalPosition body part (e.g. a Foot).</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent.</remarks>
    public static int PickBottommost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId) =>
        PickExtreme(bodyParts, entityId, preferHigher: false);

    /// <summary>Shared linear walk behind PickTopmost/PickBottommost, parameterized by comparison direction rather than duplicated per direction.</summary>
    private static int PickExtreme(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, bool preferHigher)
    {
        var bestDenseIndex = -1;
        var bestPosition = preferHigher ? -1 : byte.MaxValue + 1;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (preferHigher ? part.VerticalPosition > bestPosition : part.VerticalPosition < bestPosition)
            {
                bestPosition = part.VerticalPosition;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }

    /// <summary>Picks entityId's first body part of the requested type.</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent of that type -- the expected "no Foot on this race" outcome, not an error case.</remarks>
    public static int PickByType(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartType type)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (bodyParts.GetReadonlyByDenseIndex(denseIndex).Type == type)
            {
                return denseIndex;
            }
        }

        return -1;
    }

    /// <summary>Picks entityId's body part matching rule.PreferredType, falling back to rule.Fallback's own selection when no part of that type exists (or rule.PreferredType is null -- no type preference at all, e.g. lava's generic bottom-up targeting).</summary>
    public static int PickByTypeWithFallback(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartTargetRule rule, MathUtility mathUtility)
    {
        var typeMatch = rule.PreferredType is { } type ? PickByType(bodyParts, entityId, type) : -1;
        if (typeMatch != -1)
        {
            return typeMatch;
        }

        return rule.Fallback switch
        {
            BodyPartFallback.Topmost => PickTopmost(bodyParts, entityId),
            BodyPartFallback.Bottommost => PickBottommost(bodyParts, entityId),
            _ => PickRandom(bodyParts, entityId, mathUtility),
        };
    }
}
