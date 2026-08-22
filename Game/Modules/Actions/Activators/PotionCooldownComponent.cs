using Engine.ECS.Components;

namespace Game.Modules.Actions.Activators;

/// <summary>Represents the cooldown state for a potion.</summary>
/// <param name="totalFrames">The total number of frames before it's safe to drink another potion.</param>
/// <param name="framesRemaining">The number of frames remaining in the cooldown.</param>
/// <cleanupVersion>1</cleanupVersion>
public struct PotionCooldownComponent(ushort totalFrames, ushort framesRemaining) : ITickCountdown
{
    public ushort TotalFrames { get; set; } = totalFrames;
    public ushort FramesRemaining { get; set; } = framesRemaining;

    ushort ITickCountdown.FramesUntilNextTick
    {
        get => FramesRemaining;
        set => FramesRemaining = value;
    }

    public override readonly string ToString() => $"{FramesRemaining}/{TotalFrames}";
}
