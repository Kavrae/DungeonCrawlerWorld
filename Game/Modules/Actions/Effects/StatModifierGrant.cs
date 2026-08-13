using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants one StatModifierComponent to context.TargetEntityId -- always the resolved target, no
/// separate Source/Target choice: a caller that wants to buff itself (e.g. a self-targeted
/// action) does so via a Self-shaped TargetingSpec, which already resolves TargetEntityId to the
/// caster, the same way every other effect entry reads "who this lands on" (see AuraSourceGrant's
/// own doc comment for the identical reasoning). No-op when context.StatModifiers isn't wired.
/// DurationFrames is scaled by context.DurationScaleMultiplier (a ScrollActivator activation sets
/// this off the caster's Intelligence -- see ScrollScalingEffects; every other activator leaves
/// it at the default 1.0, a no-op) -- guarded so DurationFrames' own Permanent sentinel (-1, see
/// StatModifierComponent) is never multiplied into a meaningless negative number.
/// </summary>
public sealed record StatModifierGrant(
    StatModifierTarget Target,
    StatModifierOperation Operation,
    StatModifierPolarity Polarity,
    bool CanModify,
    float Magnitude,
    int DurationFrames) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.StatModifiers is null)
        {
            return;
        }

        var durationFrames = DurationFrames > 0
            ? (int)Math.Round(DurationFrames * context.DurationScaleMultiplier)
            : DurationFrames;

        context.StatModifiers.Add(context.TargetEntityId, new StatModifierComponent(
            Target, Operation, Polarity, CanModify, Magnitude, durationFrames, StatusEffectSource.FromEntity(context.SourceEntityId)));
    }
}
