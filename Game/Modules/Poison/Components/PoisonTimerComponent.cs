using Game.World;

namespace Game.Modules.Poison.Components;

/// <summary>
/// Present on an entity only while it currently has at least one Poison stack -- added on the
/// 0-to-1 stack transition, removed only when RemainingDurationTicks reaches 0.
/// </summary>
public struct PoisonTimerComponent(int framesUntilNextTick, int stackCount, int remainingDurationTicks, StatusEffectSource source)
{
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;

    public int StackCount { get; set; } = stackCount;

    public int RemainingDurationTicks { get; set; } = remainingDurationTicks;

    public StatusEffectSource Source { get; set; } = source;
}
