using Game.Modules.Mana;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>Takes Amount mana from context.TargetEntityId, and can be applied only to a target that has that much.</summary>
/// <remarks>
/// A cost or an upkeep written as an effect, through Mana's own ManaCost: CanApply refuses a target with no mana pool, or with less
/// than Amount left after what earlier entries in the same list reserved, so something that asks first
/// (a toggle's activation and periodic effects) is refused rather than draining what little is there. Amount is multiplied by context.Magnitude.
/// Applied without asking, it takes what the target has, down to none. The scaled amount is never rounded: mana is held as a
/// float, so a cost a modifier halves takes half as much -- a 1-mana upkeep cut in half is 0.5 a second, not free.
/// </remarks>
public sealed record ManaDrain(ushort Amount) : IResourceDrain
{
    public DrainedResource Resource => DrainedResource.Mana;

    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingManaDrain, StatModifierTarget.IncomingManaDrain)];

    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => Modifiers;

    public EffectRefusal CanApply(in EffectContext context, ref EffectReservations reservations)
    {
        var mana = context.Services.Mana;
        if (!ManaCost.HasMana(mana, context.TargetEntityId))
        {
            return EffectRefusal.NoManaPool;
        }

        var amount = AmountFor(in context);
        if (!ManaCost.HasEnough(mana, context.TargetEntityId, reservations.ReservedMana + amount))
        {
            return EffectRefusal.NotEnoughMana;
        }

        reservations.ReserveMana(amount);
        return EffectRefusal.None;
    }

    public EffectOutcome Apply(in EffectContext context)
    {
        var amount = AmountFor(in context);
        if (amount <= 0 || !ManaCost.HasMana(context.Services.Mana, context.TargetEntityId))
        {
            return EffectOutcome.NoEffect;
        }

        ManaCost.Take(context.Services.Mana, context.TargetEntityId, amount, context.Services.StatModifiers);
        return EffectOutcome.Applied;
    }

    /// <summary>The mana this takes from context.TargetEntityId: Amount times context.Magnitude, through the ManaDrain modifiers on both ends, never rounded.</summary>
    public float AmountFor(in EffectContext context) =>
        MathF.Max(0f, EffectModifiers.Scale(in context, Amount * context.Magnitude, StatModifierTarget.OutgoingManaDrain, StatModifierTarget.IncomingManaDrain));
}
