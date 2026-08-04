using Engine.ECS.Components.Stores;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.StatModifiers;

/// <summary>
/// Combines a stat's base value with whichever StatModifierComponents currently target it --
/// recomputed fresh on every call from the stat's own untouched base, never cached (see
/// StatModifierComponent's own doc comment for why). Formula: (base + sum of additive
/// magnitudes) * (1 + sum of multiplicative magnitudes) -- additive first, then multiplicative,
/// per TODO.md's documented order. A multiplicative magnitude is the decimal delta from 1.0
/// (e.g. +100% = 1.0, -100% = -1.0), and multiple multiplicative modifiers on the same target
/// sum their deltas before the single (1 + sum) is applied once, rather than compounding.
/// </summary>
public static class StatModifierMath
{
    /// <summary>pool may be null -- callers that build a module set without StatModifiersModule (common across smaller, isolated tests) still work, just with no possible active modifiers, so this returns baseValue unchanged rather than requiring every such caller to special-case a missing pool itself.</summary>
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

        return (baseValue + additiveSum) * (1f + multiplicativeSum);
    }

    /// <summary>Same as GetEffectiveValue, but for two targets in a single walk of the entity's modifier chain -- for callers (e.g. HealthRegenSystem, needing both HealthRegen and MaximumHealth) that would otherwise walk the same chain twice per entity per cycle.</summary>
    public static (float First, float Second) GetEffectiveValues(MultiComponentPool<StatModifierComponent>? pool, int entityId, StatModifierTarget firstTarget, float firstBaseValue, StatModifierTarget secondTarget, float secondBaseValue)
    {
        if (pool is null)
        {
            return (firstBaseValue, secondBaseValue);
        }

        var firstAdditiveSum = 0f;
        var firstMultiplicativeSum = 0f;
        var secondAdditiveSum = 0f;
        var secondMultiplicativeSum = 0f;

        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            ref readonly var modifier = ref pool.GetReadonlyByDenseIndex(denseIndex);

            if (modifier.Target == firstTarget)
            {
                if (modifier.Operation == StatModifierOperation.Additive)
                {
                    firstAdditiveSum += modifier.Magnitude;
                }
                else
                {
                    firstMultiplicativeSum += modifier.Magnitude;
                }
            }
            else if (modifier.Target == secondTarget)
            {
                if (modifier.Operation == StatModifierOperation.Additive)
                {
                    secondAdditiveSum += modifier.Magnitude;
                }
                else
                {
                    secondMultiplicativeSum += modifier.Magnitude;
                }
            }
        }

        return (
            (firstBaseValue + firstAdditiveSum) * (1f + firstMultiplicativeSum),
            (secondBaseValue + secondAdditiveSum) * (1f + secondMultiplicativeSum));
    }
}
