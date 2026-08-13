using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants (or, permanent-only, flip-toggles) a StatusEffectAuraSourceComponent of
/// StatusEffectType on context.TargetEntityId -- always the resolved target, no separate
/// Source/Target choice: a caller that wants to target itself (e.g. Toxic Idol) does so by using
/// a Self-shaped TargetingSpec, which already resolves TargetEntityId to the caster, the same way
/// every other effect entry reads "who this lands on." The component itself already exists and is
/// already wired into a working system (Lava uses it today via Game/Blueprints/Terrain/Lava.cs);
/// this entry is only the missing add/remove switch a creature-cast action needs, not a
/// reimplementation of any aura behavior -- everything downstream (radiating, re-granting nearby
/// entities, keeping AuraGrid/MapTintGrid in sync) stays entirely inside AuraSourceEffects and the
/// systems that react to its events.
///
/// Two distinct modes, chosen by whether DurationFrames is set:
/// - null (default) -- permanent flip-toggle (AuraSourceEffects.Toggle): unblocks TODO.md's
///   "Toggle poison aura ability" (Toxic Idol). Re-applying removes it; well-behaved only on a
///   single-resolution activator (e.g. Self-targeted) -- a multi-target activator would call
///   Apply once per resolved target, each with its own TargetEntityId, so it flips each target
///   independently rather than the same entity on/off/on/off -- almost certainly still not what a
///   multi-target permanent toggle wants, the action author's responsibility per the "composition
///   order is meaningful" rule.
/// - non-null -- a timed grant (AuraSourceEffects.Apply, never flips, refreshes on re-apply)
///   plus an AuraSourceExpiryComponent so AuraSourceExpirySystem revokes it once DurationFrames
///   (scaled by context.DurationScaleMultiplier, same as StatModifierGrant's own duration --
///   a ScrollActivator activation sets this off the caster's Intelligence, every other activator
///   leaves it at the default 1.0, a no-op) runs out. Scroll of Torch is the concrete user
///   (StatusEffectType.Light).
/// </summary>
public sealed record AuraSourceGrant(
    StatusEffectType StatusEffectType,
    int AuraAndGlowStrength,
    Color GlowColor,
    int? DurationFrames = null) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.AuraSources is null)
        {
            return;
        }

        if (DurationFrames is not { } durationFrames)
        {
            AuraSourceEffects.Toggle(context.AuraSources, context.EventBus, context.TargetEntityId, StatusEffectType, AuraAndGlowStrength, GlowColor);
            return;
        }

        var scaledDurationFrames = (int)Math.Round(durationFrames * context.DurationScaleMultiplier);

        AuraSourceEffects.Apply(context.AuraSources, context.EventBus, context.TargetEntityId, StatusEffectType, AuraAndGlowStrength, GlowColor);
        context.ComponentManager.Merge(context.TargetEntityId, new AuraSourceExpiryComponent(StatusEffectType, scaledDurationFrames));
    }
}
