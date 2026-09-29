using Game.Modules.StatusEffects;

namespace Game.Modules.Actions.Effects;

/// <summary>Removes every stack of Type from the target, ending the effect -- a cure.</summary>
/// <remarks>
/// Goes through the same StatusEffectAuraApplierRegistry plugin StatusEffectGrant adds stacks through,
/// so Actions never reaches into an effect's own components. A type with no registered applier is
/// silently skipped, the same "not yet supported" treatment StatusEffectGrant gives one.
/// </remarks>
public sealed record StatusEffectRemoval(StatusEffectType Type) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.StatusEffectAppliers.TryGet(Type, out var applier))
        {
            applier.RemoveAllStacks(context.TargetEntityId);
        }
    }
}
