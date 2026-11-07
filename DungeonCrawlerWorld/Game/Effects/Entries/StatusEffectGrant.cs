using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Effects.Entries;

/// <summary>How StatusEffectGrant's StackCount is read.</summary>
public enum StatusEffectGrantMode : byte
{
    /// <summary>Adds StackCount stacks to whatever the target already holds.</summary>
    Add,

    /// <summary>Raises the target's stacks to StackCount, adding only the difference; adds nothing to a target already holding that many.</summary>
    TopUpTo,
}

/// <summary>Grants stacks of Type to the target through its registered IStatusEffectApplier.</summary>
/// <remarks>
/// <para>
/// StackCount is multiplied by context.Magnitude, scaled by Outgoing/IncomingStatusStacks (EffectModifiers) and rounded, so under an aura it follows the aura's
/// power at the target. With TopUpTo that makes the count the level the target's position
/// should carry rather than an amount per application: applying it again and again holds the stacks
/// there instead of outpacing the effect's own decay, and the decay unwinds them once the
/// applications stop.
/// </para>
/// <para>
/// BodyPart says where the stacks are held. Unspecified is the entity as a whole. Anything else
/// names one part, resolved afresh at every application, and the count and the top-up are that
/// part's alone -- so a Random grant applied each tick lands on a different part each time and the
/// parts add up. An effect that can't be held on a part (its applier ignores it) and a target with
/// no body parts both hold it on the entity.
/// </para>
/// <para>
/// A type with no registered applier is skipped (not yet supported, not an error), and so is a dead
/// target: a corpse receives no new effects, while one already running when the entity died keeps
/// ticking until it expires. StatusEffectAppliedEvent is published only when a stack landed.
/// </para>
/// <para>
/// Immunity is checked first, once: a refused grant resolves no body part, scales nothing and reads no
/// stacks -- an aura ticking on an immune target is the commonest refusal there is -- and is Refused
/// even when the target already holds every stack it could. A grant that goes ahead tells the applier
/// the check is done, so it makes one write and no second check.
/// </para>
/// <para>
/// DurationTicks is how many of the effect's own ticks it lasts, for an effect that takes a duration
/// (IStatusEffectApplier.TakesDuration; StatusEffectContentValidation refuses one for any other). It is
/// scaled by EffectDurations.Scale with the effect's polarity (StatusEffectTypeFacts.PolarityOf), never by
/// context.Magnitude. Null leaves the effect's own default. A TopUpTo grant whose target already holds its
/// stacks still refreshes the duration -- its own, or the effect's default -- so a top-up re-granted while it runs
/// (an aura's, every second) keeps it running. A duration
/// scaled to no ticks starts nothing.
/// </para>
/// </remarks>
public sealed record StatusEffectGrant(
    StatusEffectType Type,
    int StackCount = 1,
    StatusEffectGrantMode Mode = StatusEffectGrantMode.Add,
    BodyPartTargeting BodyPart = default,
    ushort? DurationTicks = null) : IEffectEntry
{
    private static readonly (StatModifierTarget, StatModifierTarget) StackModifiers = (StatModifierTarget.OutgoingStatusStacks, StatModifierTarget.IncomingStatusStacks);

    private static readonly (StatModifierTarget, StatModifierTarget)[] UntimedModifiers = [StackModifiers];

    private static readonly (StatModifierTarget, StatModifierTarget)[] TimedBuffModifiers = [StackModifiers, .. EffectDurations.ModifiersFor(StatModifierPolarity.Buff)];

    private static readonly (StatModifierTarget, StatModifierTarget)[] TimedDebuffModifiers = [StackModifiers, .. EffectDurations.ModifiersFor(StatModifierPolarity.Debuff)];

    /// <remarks>The stacks, and a duration the grant names by the effect's polarity.</remarks>
    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers =>
        DurationTicks is null ? UntimedModifiers
        : StatusEffectTypeFacts.PolarityOf(Type) == StatModifierPolarity.Buff ? TimedBuffModifiers
        : TimedDebuffModifiers;

    public EffectOutcome Apply(in EffectContext context)
    {
        var services = context.Services;
        var targetEntityId = context.TargetEntityId;

        if (services.DeadEntities.Has(targetEntityId) || !services.StatusEffectAppliers.TryGet(Type, out var applier))
        {
            return EffectOutcome.NoEffect;
        }

        if (StatusEffectImmunity.IsImmune(services.StatusEffectImmunities, targetEntityId, Type, context.Source, services.EventBus, services.PlayerQuery, context.AnnouncesRefusal))
        {
            StatusEffectGrantFloatingText.Publish(services.FloatingTextFeed, Type, targetEntityId, StatusEffectStacksApplied.Refused, context.AnnouncesRefusal);
            return EffectOutcome.Refused;
        }

        var bodyPartId = BodyPart.ResolvePartId(in context);

        int stacksToGrant = EffectModifiers.ScaleToUShort(in context, StackCount * context.Magnitude, StatModifierTarget.OutgoingStatusStacks, StatModifierTarget.IncomingStatusStacks);
        if (Mode == StatusEffectGrantMode.TopUpTo)
        {
            stacksToGrant -= applier.GetCurrentStackCount(targetEntityId, bodyPartId);
        }

        var durationTicks = EffectDurations.Scale(in context, DurationTicks, StatusEffectTypeFacts.PolarityOf(Type));

        if (stacksToGrant <= 0)
        {
            if (Mode == StatusEffectGrantMode.TopUpTo && applier.TakesDuration)
            {
                applier.ApplyStacks(targetEntityId, 0, context.Source, context.Now, context.AnnouncesRefusal, bodyPartId, immunityAlreadyChecked: true, durationTicks);
            }

            return EffectOutcome.NoEffect;
        }

        var applied = applier.ApplyStacks(targetEntityId, stacksToGrant, context.Source, context.Now, context.AnnouncesRefusal, bodyPartId, immunityAlreadyChecked: true, durationTicks);
        StatusEffectGrantFloatingText.Publish(services.FloatingTextFeed, Type, targetEntityId, applied, context.AnnouncesRefusal);

        if (applied.StacksLanded > 0)
        {
            services.EventBus.Publish(new StatusEffectAppliedEvent(targetEntityId, Type, context.Source));
            return EffectOutcome.Applied;
        }

        return EffectOutcome.NoEffect;
    }
}
