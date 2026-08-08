using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.Burning.Components;

/// <summary>
/// Present on an entity only while it currently has at least one Burning stack -- added on the
/// 0-to-1 stack transition, removed once stacks reach 0 (see BurningSystem). Countdown to the
/// next damage tick; gaining an additional stack while already burning must not reset it.
/// </summary>
public struct BurningTimerComponent(int framesUntilNextTick, int stackCount) : ITickCountdown, IStatusEffectStackCount
{
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;

    /// <summary>
    /// Cached running total of this entity's Burning stacks, kept in sync by
    /// BurningEffects.ApplyStack (increment) and BurningSystem.Tick (decrement)
    /// </summary>
    public int StackCount { get; set; } = stackCount;

    public override readonly string ToString() => $"FramesUntilNextTick : {FramesUntilNextTick}\nStackCount : {StackCount}";
}
