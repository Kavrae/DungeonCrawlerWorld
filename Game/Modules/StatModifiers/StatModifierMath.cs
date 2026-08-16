using Engine.ECS.Components.Stores;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.StatModifiers;

/// <summary>Provides mathematical operations for calculating effective stat values with modifiers.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class StatModifierMath
{
    /// <summary>Calculates the effective value of a stat with the given modifiers.</summary>
    /// <remarks>Additive modifiers are all applied before multiplicative modifiers.</remarks>
    /// <param name="pool">The pool of stat modifiers.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="target">The target stat.</param>
    /// <param name="baseValue">The base value of the stat.</param>
    /// <returns>The effective value of the stat.</returns>
    public static float GetEffectiveValue(MultiComponentPool<StatModifierComponent>? pool, int entityId, StatModifierTarget target, float baseValue)
    {
        if (pool is null)
        {
            return baseValue;
        }

        var additiveSum = 0f;
        var multiplicativeSum = 0f;

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target != target)
            {
                continue;
            }

            if (modifier.Operation == StatModifierOperation.Additive)
            {
                additiveSum += modifier.Magnitude;
            }
            else
            {
                multiplicativeSum += modifier.Magnitude;
            }
        }

        return CalculateTotal(baseValue, additiveSum, multiplicativeSum);
    }

    /// <summary>Same as GetEffectiveValue, but for any number of targets in a single walk of the entity's modifier chain -- for callers (e.g. HealthRegenSystem, needing both HealthRegen and MaximumHealth) that would otherwise walk the same chain once per target per cycle.</summary>
    /// <remarks>
    /// destination receives each pairs entry's effective value at the same index -- a
    /// caller-owned buffer rather than an allocated return array, matching this codebase's
    /// convention for a hot per-entity per-frame path (see
    /// MultiComponentPool.CopyAll/StatusEffectQueries.GetActiveEffectTypes). pairs and
    /// destination must be the same length.
    /// </remarks>
    /// <param name="pool">The pool of stat modifiers.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="pairs">Each target stat and its base value.</param>
    /// <param name="destination">Receives each pairs entry's effective value, at the same index.</param>
    public static void GetEffectiveValues(MultiComponentPool<StatModifierComponent>? pool, int entityId, ReadOnlySpan<(StatModifierTarget Target, float BaseValue)> pairs, Span<float> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(pairs.Length, destination.Length);

        if (pool is null)
        {
            for (var i = 0; i < pairs.Length; i++)
            {
                destination[i] = pairs[i].BaseValue;
            }

            return;
        }

        Span<float> additiveSums = stackalloc float[pairs.Length];
        Span<float> multiplicativeSums = stackalloc float[pairs.Length];

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);

            for (var i = 0; i < pairs.Length; i++)
            {
                if (modifier.Target != pairs[i].Target)
                {
                    continue;
                }

                if (modifier.Operation == StatModifierOperation.Additive)
                {
                    additiveSums[i] += modifier.Magnitude;
                }
                else
                {
                    multiplicativeSums[i] += modifier.Magnitude;
                }

                break;
            }
        }

        for (var i = 0; i < pairs.Length; i++)
        {
            destination[i] = CalculateTotal(pairs[i].BaseValue, additiveSums[i], multiplicativeSums[i]);
        }
    }

    /// <summary>Applies a base value's accumulated additive and multiplicative modifier sums.</summary>
    /// <remarks>Additive first, then multiplicative, per TODO.md's documented order -- see this class's own doc comment for the full formula rationale.</remarks>
    private static float CalculateTotal(float baseValue, float additiveSum, float multiplicativeSum) => (baseValue + additiveSum) * (1f + multiplicativeSum);
}
