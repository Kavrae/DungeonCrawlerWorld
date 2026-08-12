using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Toggles StatusEffectAuraSourceComponent on the SOURCE entity (the caster) -- unblocks
/// TODO.md's "Toggle poison aura ability". The component itself already exists and is already
/// wired into a working system (Lava uses it today via Game/Blueprints/Terrain/Lava.cs); this
/// entry is only the missing on/off switch a creature-cast action needs to add/remove it, not a
/// reimplementation of any aura behavior -- everything downstream (radiating, re-granting nearby
/// entities) stays entirely inside the existing AuraGrid/StatusEffectAuraSystem.
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

        if (context.AuraSources.Has(context.SourceEntityId))
        {
            context.AuraSources.Remove(context.SourceEntityId);
        }
        else
        {
            context.AuraSources.Merge(context.SourceEntityId, new StatusEffectAuraSourceComponent(Type, AuraAndGlowStrength, GlowColor));
        }
    }
}
