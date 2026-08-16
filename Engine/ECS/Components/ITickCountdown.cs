namespace Engine.ECS.Components;

/// <summary>Represents a countdown until the next periodic tick.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface ITickCountdown
{
    ushort FramesUntilNextTick { get; set; }
}
