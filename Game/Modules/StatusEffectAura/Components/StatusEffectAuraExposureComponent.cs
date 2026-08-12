using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// One instance per (entity, EffectType) currently within range of at least one
/// StatusEffectAuraSourceComponent of that type -- added/refreshed on entering range, removed
/// on leaving (see StatusEffectAuraSystem). Mirrors StatusEffectAuraSourceComponent's own
/// per-type-instance shape (a MultiComponentPool keyed by entity, EffectType as a field found
/// via a dense-chain walk) rather than one shared flag per entity: an entity can be in range of
/// several different effect types at once, each with its own independent tick countdown, so a
/// newly-in-range type is never gated behind whether some OTHER type already has a running
/// exposure. StatusEffectAuraSystem always re-resolves "how many stacks" fresh from AuraGrid
/// rather than trusting a stale snapshot -- this component exists purely to drive each type's
/// own tick countdown.
/// </summary>
public struct StatusEffectAuraExposureComponent(StatusEffectType effectType, int framesUntilNextTick) : ITickCountdown
{
    public StatusEffectType EffectType { get; set; } = effectType;
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;

    public override readonly string ToString() => $"EffectType : {EffectType}\nFramesUntilNextTick : {FramesUntilNextTick}";
}
