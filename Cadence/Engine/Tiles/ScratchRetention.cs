namespace Engine.Tiles;

/// <summary>How much reusable per-tile scratch the Tiles code keeps between calls.</summary>
/// <remarks>Scratch grows to the largest area asked for and is kept per thread; past this many tiles it is dropped after use, so one very long range doesn't pin its buffers for good.</remarks>
internal static class ScratchRetention
{
    /// <summary>The most tiles a scratch buffer keeps after use: 256 x 256, a range of about 127 tiles.</summary>
    public const int KeptCellCount = 256 * 256;
}
