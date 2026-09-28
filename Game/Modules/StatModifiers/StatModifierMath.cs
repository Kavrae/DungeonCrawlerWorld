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
    /// <param name="activeTags">The current activation's own Tags (e.g. ActionEffectContext.ActivatorTags) -- a modifier with a non-null ConditionTag only contributes when activeTags contains it; null (the default) means only unconditional modifiers apply.</param>
    /// <returns>The effective value of the stat.</returns>
    public static float GetEffectiveValue(MultiComponentPool<StatModifierComponent> pool, int entityId, StatModifierTarget target, float baseValue, IReadOnlyList<Tag>? activeTags = null)
    {
        var additiveSum = 0f;
        var multiplicativeSum = 0f;

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target != target)
            {
                continue;
            }

            if (modifier.ConditionTag is { } conditionTag && (activeTags is null || !activeTags.Contains(conditionTag)))
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

    /// <summary>Same as GetEffectiveValue, but for any number of targets in a single walk of the entity's modifier chain -- for callers (e.g. SimpleHealthRegenSystem, needing both HealthRegen and MaximumHealth) that would otherwise walk the same chain once per target per cycle.</summary>
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
    /// <param name="activeTags">Same meaning as GetEffectiveValue's own activeTags parameter, applied uniformly across every pair.</param>
    public static void GetEffectiveValues(MultiComponentPool<StatModifierComponent> pool, int entityId, ReadOnlySpan<(StatModifierTarget Target, float BaseValue)> pairs, Span<float> destination, IReadOnlyList<Tag>? activeTags = null)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(pairs.Length, destination.Length);

        Span<float> additiveSums = stackalloc float[pairs.Length];
        Span<float> multiplicativeSums = stackalloc float[pairs.Length];

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);

            if (modifier.ConditionTag is { } conditionTag && (activeTags is null || !activeTags.Contains(conditionTag)))
            {
                continue;
            }

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

    /// <summary>The entity's accumulated additive and multiplicative sums for one target, before any base value is folded in.</summary>
    /// <remarks>
    /// Two floats are enough to reproduce the target's effect on <i>any</i> base value, because
    /// CalculateTotal is (base + additive) * (1 + multiplicative) and multiplicative modifiers are
    /// summed rather than compounded. That is what lets a caller snapshot an entity's whole
    /// modifier state for a target before changing it and reconstruct every affected value's old
    /// result afterwards -- see MaximumHealthShift, which does exactly that across a body part list.
    ///
    /// Unconditional modifiers only: a ConditionTag modifier depends on the activation being
    /// resolved, which a snapshot has no notion of.
    /// </remarks>
    public static void GetSums(MultiComponentPool<StatModifierComponent> pool, int entityId, StatModifierTarget target, out float additiveSum, out float multiplicativeSum)
    {
        additiveSum = 0f;
        multiplicativeSum = 0f;

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);
            if (modifier.Target != target || modifier.ConditionTag is not null)
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
    }

    /// <summary>Applies a base value's accumulated additive and multiplicative modifier sums.</summary>
    /// <remarks>Additive first, then multiplicative, per TODO.md's documented order -- see this class's own doc comment for the full formula rationale.</remarks>
    public static float CalculateTotal(float baseValue, float additiveSum, float multiplicativeSum) => (baseValue + additiveSum) * (1f + multiplicativeSum);
}
