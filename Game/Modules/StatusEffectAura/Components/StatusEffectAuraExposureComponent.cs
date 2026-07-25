using Engine.ECS.Components;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// Present on an entity only while it currently sits within range of at least one
/// StatusEffectAuraSourceComponent of any effect type -- added/refreshed on entering range,
/// removed on leaving (see StatusEffectAuraSystem). Deliberately doesn't record which effect
/// type(s) or source(s) granted it: a source can itself be a moving entity, and an entity can
/// in principle be in range of sources of several different effect types at once, so
/// StatusEffectAuraSystem always re-resolves "which effect types, how many stacks each" fresh
/// from its per-effect-type AuraGrids rather than trusting a stale snapshot -- this component
/// exists purely to drive the shared tick countdown.
/// </summary>
public struct StatusEffectAuraExposureComponent(int framesUntilNextTick) : ITickCountdown
{
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;
}
