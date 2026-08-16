namespace Game.Modules.Actions.Effects;

/// <summary>
/// Probability-gated trigger for one or more further ActionEffects, applied to the same
/// source/target via the same ActionEffectSequence both IActionActivator orchestration and this
/// entry share. MaxChainDepth guards the same failure mode WoW/PoE explicitly design around: a
/// proc that (directly or via a longer cycle) triggers itself -- since a ChainedEffect can
/// itself appear inside one of its own TriggeredEffects, arbitrary-depth chaining falls out for
/// free from ordinary composition, so the depth guard is the only extra safety needed.
/// </summary>
public sealed record ChainedEffect(float TriggerChance, IReadOnlyList<ActionEffect> TriggeredEffects) : IActionEffectEntry
{
    public const byte MaxChainDepth = 5;

    public void Apply(ActionEffectContext context)
    {
        if (context.ChainDepth >= MaxChainDepth || context.MathUtility.NextDouble() >= TriggerChance)
        {
            return;
        }

        ActionEffectSequence.Apply(TriggeredEffects, context with { ChainDepth = (byte)(context.ChainDepth + 1) });
    }
}
