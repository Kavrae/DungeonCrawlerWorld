using Engine.ECS.Components;

namespace Game.Modules.Actions.Components;

/// <summary>
/// Written by DodgeAction's own effect (DodgeActivation) while the entity is immune to any
/// Dodgeable-tagged action's effects -- see ActionEffectResolver.Apply's own skip check. Ticked
/// down and removed by DodgeExpirySystem once FramesRemaining reaches 0. At most one per entity
/// (a fresh Dodge simply refreshes the window via Merge's replace policy -- see ActionsModule's
/// own registration).
/// </summary>
/// <remarks>An expiring duration: a timer (IScheduledTimer) whose one firing removes it -- see DodgeExpirySystem.</remarks>
/// <param name="expiresAtFrame">The simulation frame the window closes on -- FrameDeadline.After(now, windowFrames).</param>
public struct DodgingComponent(uint expiresAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame this dodge window closes on.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"ExpiresAtFrame : {ExpiresAtFrame}";
}
