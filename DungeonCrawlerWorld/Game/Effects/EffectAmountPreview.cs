using System.Diagnostics.CodeAnalysis;
using Game.Modules.StatModifiers;

namespace Game.Effects;

/// <summary>An amount an effect entry carries, as an entry previews it (IEffectEntry.PreviewAmounts).</summary>
public enum EffectAmount : byte
{
    DamageMinimum,
    DamageMaximum,
    DamagePercentOfMaximumHealth,
    HealFlat,
    HealPercentOfMaximumHealth,
    ManaRestoredFraction,
    ResourceDrained,
    Stacks,
    Duration,
    AuraPower,
    AuraSize,
    ProcChance,
}

/// <summary>A value as it would be when used, its base, and every step between them, in order.</summary>
public sealed record PreviewedValue(float Base, float Value, IReadOnlyList<ValueContribution> Contributions)
{
    /// <summary>Whether anything changed the value from its base.</summary>
    public bool IsModified => Value != Base;
}

/// <summary>The amounts one effect entry carries, worked out as they would be when used, each with what changed it.</summary>
/// <remarks>
/// An entry fills it in IEffectEntry.PreviewAmounts: Begin an amount with its base, compute it through the same helpers
/// its Apply uses -- which record each modifier and ability score they fold in while the context carries this preview
/// (EffectContext.AmountPreview) -- then End it with the result.
/// </remarks>
public sealed class EffectAmountPreview
{
    private readonly List<(EffectAmount Amount, PreviewedValue Value)> _values = [];
    private List<ValueContribution>? _contributions;
    private EffectAmount _amount;
    private float _base;

    /// <summary>Every amount worked out, in the order the entry worked them out.</summary>
    public IReadOnlyList<(EffectAmount Amount, PreviewedValue Value)> Values => _values;

    /// <summary>Where the amount being worked out records its steps; null between amounts.</summary>
    internal List<ValueContribution>? Contributions => _contributions;

    /// <summary>Starts amount from baseValue, recording magnitude as a strength when it isn't 1.</summary>
    public void Begin(EffectAmount amount, float baseValue, float magnitude = 1f)
    {
        _amount = amount;
        _base = baseValue;
        _contributions = [];
        if (magnitude != 1f)
        {
            _contributions.Add(ValueContribution.Multiplier(ValueContributionKind.Strength, magnitude));
        }
    }

    /// <summary>Records a step that isn't a modifier or an ability score (a scroll's multiplier).</summary>
    public void Add(ValueContribution contribution) => _contributions?.Add(contribution);

    /// <summary>
    /// Ends a fraction of the target's maximum -- health or mana -- that is scaled together with a flat amount: scale
    /// runs the amount's modifier chain, (amount + additive) * (1 + multiplicative). The fraction's share is
    /// scale(fraction) - scale(0), its multipliers alone, since an additive modifier adds to the amount, not to the
    /// fraction; only the multiplicative steps are kept.
    /// </summary>
    /// <param name="scaledFraction">scale(fraction), worked out while recording.</param>
    /// <param name="scaledZero">scale(0), worked out without recording (EffectContext.AmountPreview null).</param>
    public float EndFraction(float scaledFraction, float scaledZero)
    {
        _contributions?.RemoveAll(static contribution => contribution.Operation == StatModifierOperation.Additive);
        return End(scaledFraction - scaledZero);
    }

    /// <summary>Ends the amount Begin started at value, and returns value.</summary>
    public float End(float value)
    {
        _values.Add((_amount, new PreviewedValue(_base, value, _contributions ?? [])));
        _contributions = null;
        return value;
    }

    public bool TryGet(EffectAmount amount, [MaybeNullWhen(false)] out PreviewedValue value)
    {
        foreach (var (recordedAmount, recordedValue) in _values)
        {
            if (recordedAmount == amount)
            {
                value = recordedValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>amount as used, or fallback when this entry carries no such amount.</summary>
    public float ValueOr(EffectAmount amount, float fallback) => TryGet(amount, out var value) ? value.Value : fallback;
}
