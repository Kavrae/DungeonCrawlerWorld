using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.Paralysis.Components;

/// <summary>
/// Present on an entity only while Paralysis is active -- added on grant, removed on the frame it
/// expires (see ParalysisSystem). Unlike Burning/Poison there's no repeating action partway
/// through, so its one timer-wheel firing is the expiry.
/// </summary>
/// <param name="expiresAtFrame">The simulation frame Paralysis ends on -- FrameDeadline.After(now, duration).</param>
public struct ParalysisTimerComponent(uint expiresAtFrame) : IScheduledTimer, IStatusEffectStackCount
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame Paralysis ends on.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    public readonly byte StackCount => 1;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"ExpiresAtFrame : {ExpiresAtFrame}\nStackCount : {StackCount}";
}
