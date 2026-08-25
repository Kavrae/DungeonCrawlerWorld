using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Health.Components;

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

    /// <summary>Picks entityId's body part with the lowest CurrentHealth/MaximumHealth fraction, skipping any part still inside its post-disable lockout window.</summary>
    /// <remarks>
    /// The yo-yo-prevention case RegenLockoutFramesRemaining exists for. Its only caller is
    /// ComplexHealthRegenSystem's own passive-regen tick -- an active heal (potion/scroll) never
    /// goes through this method at all, see ComplexHealthHeal.ApplyFractionToAllParts, which heals
    /// every part at once rather than picking one, so there is no "should this ignore the lockout"
    /// question for the heal path to begin with. Returns -1 if entityId owns no BodyPartComponent,
    /// or every part is either at full health or currently locked out.
    /// </remarks>
    public static int PickLowestPercentage(MultiComponentPool<BodyPartComponent> bodyParts, int entityId)
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

            var fraction = part.MaximumHealth > 0 ? part.CurrentHealth / part.MaximumHealth : 1f;
            if (fraction >= 1f)
            {
                continue; // Already full, nothing to gain by selecting it.
            }

            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }
}
