using Game.Modules.StatModifiers;


namespace Game.Effects.Entries;

/// <summary>
/// Probability-gated trigger for one or more further Effects, applied to the same
/// source/target via the same EffectSequence both IActionActivator orchestration and this
/// entry share. MaxChainDepth guards the same failure mode WoW/PoE explicitly design around: a
/// proc that (directly or via a longer cycle) triggers itself -- since a ChainedEffect can
/// itself appear inside one of its own TriggeredEffects, arbitrary-depth chaining falls out for
/// free from ordinary composition, so the depth guard is the only extra safety needed.
/// </summary>
public sealed record ChainedEffect(float TriggerChance, IReadOnlyList<Effect> TriggeredEffects) : IEffectEntry
{
    public const byte MaxChainDepth = 5;

    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingProcChance, StatModifierTarget.IncomingProcChance)];

    public IReadOnlyList<Effect> NestedEffects => TriggeredEffects;

    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => Modifiers;

    /// <remarks>TriggerChance is scaled by Outgoing/IncomingProcChance (EffectModifiers) before it is rolled.</remarks>
    public EffectOutcome Apply(in EffectContext context)
    {
        if (context.ChainDepth >= MaxChainDepth ||
            context.Services.MathUtility.NextDouble() >= EffectModifiers.Scale(in context, TriggerChance, StatModifierTarget.OutgoingProcChance, StatModifierTarget.IncomingProcChance))
        {
            return EffectOutcome.NoEffect;
        }

        return EffectSequence.Apply(TriggeredEffects, context with { ChainDepth = (byte)(context.ChainDepth + 1) });
    }
}
