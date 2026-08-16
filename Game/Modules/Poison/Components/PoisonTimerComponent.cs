using Engine.ECS.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Poison.Components;

/// <summary>
/// Present on an entity only while it currently has at least one Poison stack -- added on the
/// 0-to-1 stack transition, removed only when RemainingDurationTicks reaches 0.
/// </summary>
public struct PoisonTimerComponent(ushort framesUntilNextTick, byte stackCount, ushort remainingDurationTicks, StatusEffectSource source) : ITickCountdown, IStatusEffectStackCount
{
    public ushort FramesUntilNextTick { get; set; } = framesUntilNextTick;

    public byte StackCount { get; set; } = stackCount;

    public ushort RemainingDurationTicks { get; set; } = remainingDurationTicks;

    public StatusEffectSource Source { get; set; } = source;

    public override readonly string ToString() => $"FramesUntilNextTick : {FramesUntilNextTick}\nStackCount : {StackCount}\nRemainingDurationTicks : {RemainingDurationTicks}\nSource : {Source}";
}
