using Game.Modules.StatusEffects;

namespace Game.Effects.Entries;

/// <summary>Removes every stack of Type from the target, ending the effect -- a cure.</summary>
/// <remarks>
/// Goes through the same StatusEffectApplierRegistry plugin StatusEffectGrant adds stacks through,
/// so Actions never reaches into an effect's own components. A type with no registered applier is
/// silently skipped, the same "not yet supported" treatment StatusEffectGrant gives one.
/// </remarks>
public sealed record StatusEffectRemoval(StatusEffectType Type) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        if (!context.Services.StatusEffectAppliers.TryGet(Type, out var applier) || applier.GetCurrentStackCount(context.TargetEntityId) == 0)
        {
            return EffectOutcome.NoEffect;
        }

        applier.RemoveAllStacks(context.TargetEntityId);
        return EffectOutcome.Applied;
    }
}
