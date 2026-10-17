using Engine.Utilities;

namespace Game;

/// <summary>DungeonCrawlerWorld's update rate: what GameTiming holds once Apply has run.</summary>
/// <remarks>A constant, so content durations stay compile-time constants; every executable and test assembly calls Apply first thing.</remarks>
public static class GameFrameRate
{
    public const ushort FramesPerSecond = 60;

    /// <summary>Hands FramesPerSecond to the engine's GameTiming.</summary>
    public static void Apply() => GameTiming.SetFramesPerSecond(FramesPerSecond);
}
