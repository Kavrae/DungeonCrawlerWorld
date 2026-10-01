using Engine.ECS.Systems;
using Game.Effects;
using Game.Modules.Auras.Components;

namespace Game.Modules.Auras;

/// <summary>
/// Grants (or, permanent-only, flip-toggles) a source of Aura on
/// context.TargetEntityId -- always the resolved target, no separate Source/Target choice: a
/// caller that wants to target itself (e.g. Toxic Idol) does so by using a Self-shaped
/// TargetingSpec, which already resolves TargetEntityId to the caster, the same way every other
/// effect entry reads "who this lands on." This entry is only the add/remove switch: everything
/// downstream (radiating, exposing nearby entities, keeping the AuraField in step) stays
/// inside AuraSourceEffects and AuraSystem.
///
/// Two distinct modes, chosen by whether DurationFrames is set:
/// - null (default) -- permanent flip-toggle (AuraSourceEffects.Toggle), e.g. Toxic Idol.
///   Re-applying removes it; well-behaved only on a single-resolution activator (e.g.
///   Self-targeted) -- a multi-target activator would call Apply once per resolved target, each
///   with its own TargetEntityId, so it flips each target independently rather than the same
///   entity on/off/on/off -- almost certainly still not what a multi-target permanent toggle
///   wants, the action author's responsibility per the "composition order is meaningful" rule.
/// - non-null -- a timed grant (AuraSourceEffects.Apply, never flips, refreshes on re-apply)
///   plus an AuraSourceExpiryComponent so AuraSourceExpirySystem revokes it once DurationFrames
///   (scaled by context.DurationScaleMultiplier, same as StatModifierGrant's own duration --
///   a ScrollActivator activation sets this off the caster's Intelligence, every other activator
///   leaves it at the default 1.0, a no-op) runs out. Scroll of Torch is the concrete user
///   (its own light aura).
/// </summary>
/// <param name="Aura">The aura's definition, declared with whatever grants it: what the aura does and the colour it glows.</param>
/// <param name="Strength">The source's strength, which also sets its reach (see AuraSourceComponent).</param>
public sealed record AuraSourceGrant(
    AuraDefinition Aura,
    byte Strength,
    ushort? DurationFrames = null) : IEffectEntry
{
    public IEnumerable<AuraDefinition> ReferencedAuras => [Aura];

    public EffectOutcome Apply(in EffectContext context)
    {
        if (DurationFrames is not { } durationFrames)
        {
            context.Services.AuraSources.Toggle(context.TargetEntityId, Aura, Strength);
            return EffectOutcome.Applied;
        }

        var scaledDurationFrames = (ushort)Math.Round(durationFrames * context.DurationScaleMultiplier);

        context.Services.AuraSources.Apply(context.TargetEntityId, Aura, Strength);
        context.Services.ComponentManager.Merge(context.TargetEntityId, new AuraSourceExpiryComponent(context.Services.AuraSources.GetId(Aura), FrameDeadline.After(context.Now, scaledDurationFrames)));
        return EffectOutcome.Applied;
    }
}
