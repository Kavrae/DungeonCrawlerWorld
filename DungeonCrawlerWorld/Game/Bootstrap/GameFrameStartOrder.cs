namespace Game.Bootstrap;

/// <summary>Where each of the game's frame-start systems runs, before every module's systems: lower runs first (SystemManager.RegisterAtFrameStart).</summary>
/// <remarks>The one place these are decided, so two systems' places never depend on which was registered first or from which project.</remarks>
public static class GameFrameStartOrder
{
    /// <summary>NeighborhoodStreamer: loads and unloads neighborhoods, so its spawns are recorded by SpawnMoves the same frame.</summary>
    public const int NeighborhoodStreaming = 0;

    /// <summary>SpawnMoves: records spawns made late last frame and this frame's streaming into the moves every module's systems read.</summary>
    public const int SpawnMoves = 100;
}
