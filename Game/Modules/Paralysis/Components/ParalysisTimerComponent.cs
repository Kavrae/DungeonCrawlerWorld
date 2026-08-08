using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.Paralysis.Components;

/// <summary>
/// Present on an entity only while Paralysis is active -- added on grant, removed once the
/// countdown reaches 0 (see ParalysisSystem). FramesUntilNextTick doubles as "frames until
/// Paralysis expires": unlike Burning/Poison there's no repeating action to fire partway
/// through, so CountdownTicker.Tick's onTick fires exactly once, at expiry, and always returns
/// true (remove).
/// </summary>
public struct ParalysisTimerComponent(int framesUntilNextTick) : ITickCountdown, IStatusEffectStackCount
{
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;

    public readonly int StackCount => 1;

    public override readonly string ToString() => $"FramesUntilNextTick : {FramesUntilNextTick}\nStackCount : {StackCount}";
}
