using Game.Modules.StatusEffects;

namespace Game.Effects.Entries;

/// <summary>Removes every stack of Type from the target, ending the effect -- a cure.</summary>
/// <remarks>
/// Goes through the same StatusEffectApplierRegistry plugin StatusEffectGrant adds stacks through,
/// so Actions never reaches into an effect's own components. A type with no registered applier is
/// silently skipped, the same "not yet supported" treatment StatusEffectGrant gives one. Whether the
/// target had the effect at all is the applier's answer, since only it knows everywhere the effect
/// can be held -- a burn on one body part is still a burn.
/// </remarks>
public sealed record StatusEffectRemoval(StatusEffectType Type) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        return context.Services.StatusEffectAppliers.TryGet(Type, out var applier) && applier.RemoveAllStacks(context.TargetEntityId)
            ? EffectOutcome.Applied
            : EffectOutcome.NoEffect;
    }
}
