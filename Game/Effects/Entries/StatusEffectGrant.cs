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
/// StackCount is multiplied by context.Magnitude and rounded, so under an aura it follows the aura's
/// strength at the target. With TopUpTo that makes the count the level the target's position
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
/// ticking until it expires. The applier makes one immunity check and one write however many stacks
/// are asked for. StatusEffectAppliedEvent is published only when a stack landed.
/// </para>
/// </remarks>
public sealed record StatusEffectGrant(
    StatusEffectType Type,
    int StackCount = 1,
    StatusEffectGrantMode Mode = StatusEffectGrantMode.Add,
    BodyPartTargeting BodyPart = default) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        var services = context.Services;
        var targetEntityId = context.TargetEntityId;

        if (services.DeadEntities.Has(targetEntityId) || !services.StatusEffectAppliers.TryGet(Type, out var applier))
        {
            return EffectOutcome.NoEffect;
        }

        var bodyPartId = BodyPart.ResolvePartId(in context);

        var stacksToGrant = (int)MathF.Round(StackCount * context.Magnitude);
        if (Mode == StatusEffectGrantMode.TopUpTo)
        {
            stacksToGrant -= applier.GetCurrentStackCount(targetEntityId, bodyPartId);
        }

        if (stacksToGrant <= 0)
        {
            return EffectOutcome.NoEffect;
        }

        var stacksLanded = applier.ApplyStacks(targetEntityId, stacksToGrant, context.Source, context.Now, context.AnnouncesRefusal, bodyPartId);
        StatusEffectGrantFloatingText.Publish(services.FloatingTextFeed, services.ComponentManager, Type, targetEntityId, stacksLanded, context.AnnouncesRefusal);

        if (stacksLanded > 0)
        {
            services.EventBus.Publish(new StatusEffectAppliedEvent(targetEntityId, Type, context.Source));
            return EffectOutcome.Applied;
        }

        return StatusEffectImmunity.HasImmunity(services.ComponentManager, targetEntityId, Type) ? EffectOutcome.Refused : EffectOutcome.NoEffect;
    }
}
