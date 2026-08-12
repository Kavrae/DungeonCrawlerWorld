using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants StackCount stacks of Type to the target via the shared StatusEffectAuraApplierRegistry
/// -- the same registry/IStatusEffectAuraApplier plugin lookup Burning/Poison/Paralysis already
/// register into for aura-granted stacks, this is simply a second, direct grant path into it.
/// Silently skips any StatusEffectType with no registered applier (not an error -- "not yet
/// supported" is the same treatment StatusEffectAuraSystem gives one). Skips a dead target
/// entirely -- a corpse doesn't receive newly-granted effects; an effect already active when an
/// entity dies keeps ticking until it naturally expires, untouched here.
/// </summary>
public sealed record StatusEffectGrantEntry(StatusEffectType Type, int StackCount = 1) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (StackCount <= 0 || context.DeadEntities?.Has(context.TargetEntityId) == true || context.StatusEffectAppliers is null)
        {
            return;
        }

        if (!context.StatusEffectAppliers.TryGet(Type, out var applier))
        {
            return;
        }

        var source = StatusEffectSource.FromEntity(context.SourceEntityId);
        for (var i = 0; i < StackCount; i++)
        {
            applier.ApplyStack(context.ComponentManager, context.TargetEntityId, source);
        }

        context.EventBus.Publish(new StatusEffectAppliedEvent(context.TargetEntityId, Type, source));
    }
}
