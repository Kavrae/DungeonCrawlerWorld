using Engine.ECS.Components;

namespace Game.Modules.Actions.Activators;

/// <summary>Represents the cooldown state for a potion.</summary>
/// <remarks>
/// An expiring timer-wheel timer (IScheduledTimer): PotionCooldownSystem removes it on
/// ExpiresAtFrame. Frames remaining are derived, not stored -- PotionCooldownEffects.FramesRemaining.
/// </remarks>
/// <param name="totalFrames">The total number of frames before it's safe to drink another potion.</param>
/// <param name="expiresAtFrame">The simulation frame the cooldown ends on -- FrameDeadline.After(now, totalFrames).</param>
/// <cleanupVersion>1</cleanupVersion>
public struct PotionCooldownComponent(ushort totalFrames, uint expiresAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    public ushort TotalFrames { get; set; } = totalFrames;

    /// <summary>The simulation frame the cooldown ends on.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"Expires at {ExpiresAtFrame} ({TotalFrames} total)";
}
