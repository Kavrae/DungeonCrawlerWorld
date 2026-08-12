using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Toggles a StatusEffectAuraSourceComponent of Type on the SOURCE entity (the caster) --
/// unblocks TODO.md's "Toggle poison aura ability". The component itself already exists and is
/// already wired into a working system (Lava uses it today via Game/Blueprints/Terrain/Lava.cs);
/// this entry is only the missing on/off switch a creature-cast action needs to add/remove it,
/// not a reimplementation of any aura behavior -- everything downstream (radiating, re-granting
/// nearby entities, keeping AuraGrid/MapTintGrid in sync) stays entirely inside
/// AuraSourceEffects.Toggle and the systems that react to its events.
///
/// Always toggles the SOURCE entity, never the resolved target -- an aura radiates from whoever
/// cast it, not from whoever/whatever it happened to resolve against. Only well-behaved on a
/// Self-targeted (single-resolution) activator; a multi-target activator would call Apply once
/// per resolved target and toggle on/off/on/off within one activation, almost certainly not
/// intended -- the action author's responsibility, per the "composition order is meaningful"
/// rule.
/// </summary>
public sealed record AuraSourceToggleEntry(StatusEffectType Type, int AuraAndGlowStrength, Color GlowColor) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.AuraSources is null)
        {
            return;
        }

        AuraSourceEffects.Toggle(context.AuraSources, context.EventBus, context.SourceEntityId, Type, AuraAndGlowStrength, GlowColor);
    }
}
