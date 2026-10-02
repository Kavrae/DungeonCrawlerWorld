namespace Game.Effects;

/// <summary>Applies an ordered list of Effect, in list order.</summary>
/// <remarks>The one loop everything that applies effects shares: an action's or item's activation, and ChainedEffect's triggered effects.</remarks>
public static class EffectSequence
{
    public static EffectOutcome Apply(IReadOnlyList<Effect> effects, in EffectContext context)
    {
        var outcome = EffectOutcome.NoEffect;
        for (var index = 0; index < effects.Count; index++)
        {
            outcome = EffectOutcomes.Combine(outcome, effects[index].Apply(in context));
        }

        return outcome;
    }
}
