using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// Marks an entity (terrain, e.g. Lava, or in principle a creature) as radiating a status
/// effect aura: EffectType is which effect it grants (e.g. Burning), AuraAndGlowStrength
/// governs both how many stacks it grants and how far its visual glow reaches (the same
/// falloff -- see DistanceFalloff -- drives both, so the glow always visually matches the
/// actual gameplay reach), and GlowColor is what MapWindow lerps nearby tiles toward. A single
/// component replaces what used to be two separate ones (a Burning-specific aura source plus
/// a generic tint source) precisely because a source's gameplay reach and its glow are always
/// meant to be the same shape -- splitting them risked exactly the kind of visual/mechanical
/// mismatch a single Strength value here rules out by construction.
/// </summary>
public struct StatusEffectAuraSourceComponent(StatusEffectType effectType, int auraAndGlowStrength, Color glowColor)
{
    public StatusEffectType EffectType { get; set; } = effectType;
    public int AuraAndGlowStrength { get; set; } = auraAndGlowStrength;
    public Color GlowColor { get; set; } = glowColor;
}
