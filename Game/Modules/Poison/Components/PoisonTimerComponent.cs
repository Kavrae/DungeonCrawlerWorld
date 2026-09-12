using Engine.ECS.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Poison.Components;

/// <summary>
/// Present on an entity only while it currently has at least one Poison stack -- added on the
/// 0-to-1 stack transition, removed only when RemainingDurationTicks reaches 0.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer): writing NextTickFrame is all it takes to schedule it.</remarks>
public struct PoisonTimerComponent(uint nextTickFrame, byte stackCount, ushort remainingDurationTicks, StatusEffectSource source) : IScheduledTimer, IStatusEffectStackCount
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame of the next damage tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    public byte StackCount { get; set; } = stackCount;

    /// <summary>Damage ticks left, counting the one at NextTickFrame. Not a frame count.</summary>
    public ushort RemainingDurationTicks { get; set; } = remainingDurationTicks;

    public StatusEffectSource Source { get; set; } = source;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"NextTickFrame : {NextTickFrame}\nStackCount : {StackCount}\nRemainingDurationTicks : {RemainingDurationTicks}\nSource : {Source}";
}
