using Game.Modules.Health;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>Takes Amount health from context.TargetEntityId, and can be applied only to a target it would not kill.</summary>
/// <remarks>
/// A cost written as an effect, through Health's own HealthCost: damage reduction never applies, a body plan
/// pays an even share from every part, and nothing is rounded. CanApply refuses a target with no health, or
/// one the amount -- after what earlier entries in the same list reserved -- would kill, so a use that asks
/// first is refused rather than killing its user. Applied without asking, it takes the amount even if that
/// kills. Amount is multiplied by context.Magnitude.
/// </remarks>
public sealed record HealthDrain(ushort Amount) : IResourceDrain
{
    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingHealthDrain, StatModifierTarget.IncomingHealthDrain)];

    public DrainedResource Resource => DrainedResource.Health;

    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => Modifiers;

    public EffectRefusal CanApply(in EffectContext context, ref EffectReservations reservations)
    {
        var services = context.Services;
        if (!HealthCost.HasHealth(services.Health, services.BodyParts, context.TargetEntityId))
        {
            return EffectRefusal.NoHealth;
        }

        var amount = AmountFor(in context);
        if (!HealthCost.LeavesAlive(services.Health, services.BodyParts, context.TargetEntityId, reservations.ReservedHealth + amount))
        {
            return EffectRefusal.NotEnoughHealth;
        }

        reservations.ReserveHealth(amount);
        return EffectRefusal.None;
    }

    public EffectOutcome Apply(in EffectContext context)
    {
        var services = context.Services;
        var amount = AmountFor(in context);
        if (amount <= 0 || !HealthCost.HasHealth(services.Health, services.BodyParts, context.TargetEntityId))
        {
            return EffectOutcome.NoEffect;
        }

        HealthCost.Take(services.Health, services.BodyParts, services.StatModifiers, services.EventBus, services.PlayerQuery, services.DeadEntities, context.TargetEntityId, amount, context.Source, context.Now);
        return EffectOutcome.Applied;
    }

    /// <summary>The health this takes from context.TargetEntityId: Amount times context.Magnitude, through the HealthDrain modifiers on both ends, never rounded.</summary>
    public float AmountFor(in EffectContext context) =>
        MathF.Max(0f, EffectModifiers.Scale(in context, Amount * context.Magnitude, StatModifierTarget.OutgoingHealthDrain, StatModifierTarget.IncomingHealthDrain));
}
