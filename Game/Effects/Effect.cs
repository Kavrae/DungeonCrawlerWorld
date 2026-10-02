namespace Game.Effects;

/// <summary>A composable list of IEffectEntry.</summary>
/// <remarks>
/// Owns its own application loop; there is deliberately no separate resolver class -- Apply contains
/// no per-kind knowledge at all, every entry applies itself. Entries apply in strict list order, and
/// later entries observe the live component state earlier ones left behind, so composition order is
/// meaningful.
/// </remarks>
public sealed record Effect(IReadOnlyList<IEffectEntry> Entries)
{
    public static readonly Effect None = new([]);

    public EffectOutcome Apply(in EffectContext context)
    {
        var outcome = EffectOutcome.NoEffect;
        var entries = Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            outcome = EffectOutcomes.Combine(outcome, entries[index].Apply(in context));
        }

        return outcome;
    }
}
