using System.Runtime.CompilerServices;

namespace Game.Modules.AbilityScores.Components;

/// <summary>Every ability score an entity has, as one fixed-size struct: a base and a precomputed total per AbilityScoreType, plus which of them were granted.</summary>
/// <remarks>
/// One packed component per entity rather than one MultiComponentPool entry per score: an entity
/// that has ability scores has all seven of them, so the per-entry chain and owner bookkeeping cost
/// far more than the values themselves (36.6 MB against 2 MB of values at the entity counts
/// GameLoop.InitialEntityCapacity is sized for). The flat/multiplicative modifiers still live in
/// MultiComponentPool&lt;StatModifierComponent&gt; (filterable by the matching StatModifierTarget) --
/// this struct only holds the untouched base and the precomputed result. Granted is a bitmask
/// because a score that was never granted is not zero: readers fall back to their own defaults for
/// it (see DodgeEffects, PotionCooldownEffects).
/// </remarks>
public struct AbilityScoresComponent
{
    /// <summary>The number of AbilityScoreType members -- AbilityScoresComponentTests guards this against the enum.</summary>
    public const int Count = 7;

    [InlineArray(Count)]
    private struct Scores
    {
        private ushort _element0;
    }

    private Scores _baseValues;
    private Scores _totals;
    private byte _granted;

    public readonly bool Has(AbilityScoreType type) => (_granted & Mask(type)) != 0;

    /// <summary>The base and total for type, if it was granted.</summary>
    public readonly bool TryGet(AbilityScoreType type, out AbilityScoreValue value)
    {
        if (!Has(type))
        {
            value = default;
            return false;
        }

        value = new AbilityScoreValue(type, _baseValues[(int)type], _totals[(int)type]);
        return true;
    }

    /// <summary>Grants type, or overwrites it if it was already granted.</summary>
    public void Set(AbilityScoreType type, ushort baseValue, ushort total)
    {
        _baseValues[(int)type] = baseValue;
        _totals[(int)type] = total;
        _granted |= Mask(type);
    }

    /// <summary>Overwrites the precomputed total for type, leaving its base alone. No-ops if type was never granted.</summary>
    public void SetTotal(AbilityScoreType type, ushort total)
    {
        if (Has(type))
        {
            _totals[(int)type] = total;
        }
    }

    /// <summary>Copies every granted score of other over this one's, leaving the rest alone -- the merge a composite blueprint's later part needs.</summary>
    public void MergeFrom(in AbilityScoresComponent other)
    {
        for (var type = 0; type < Count; type++)
        {
            if ((other._granted & (1 << type)) != 0)
            {
                Set((AbilityScoreType)type, other._baseValues[type], other._totals[type]);
            }
        }
    }

    public override readonly string ToString()
    {
        var text = new System.Text.StringBuilder();
        for (var type = 0; type < Count; type++)
        {
            if ((_granted & (1 << type)) == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(", ");
            }

            text.Append((AbilityScoreType)type).Append(' ').Append(_baseValues[type]).Append('(').Append(_totals[type]).Append(')');
        }

        return text.ToString();
    }

    private static byte Mask(AbilityScoreType type) => (byte)(1 << (int)type);
}

/// <summary>One entity's base and precomputed total for one ability score, as AbilityScoreQueries hands it out.</summary>
public readonly record struct AbilityScoreValue(AbilityScoreType Type, ushort BaseValue, ushort Total)
{
    public override string ToString() => $"{Type} : {BaseValue}({Total})";
}
