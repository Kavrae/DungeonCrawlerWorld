using Game.Floors;

namespace Game.Admin;

/// <summary>Admin Mode's "Regenerate" command: rebuilds a loaded neighborhood from a fresh visit.</summary>
/// <remarks>A debugging tool over NeighborhoodStreamer's regenerate job, not a gameplay path.</remarks>
public sealed class NeighborhoodAdminCommands(NeighborhoodStreamer neighborhoodStreamer)
{
    /// <inheritdoc cref="NeighborhoodStreamer.CanRegenerate"/>
    public bool CanRegenerate(int cellX, int cellY, out string reason) => neighborhoodStreamer.CanRegenerate(cellX, cellY, out reason);

    /// <inheritdoc cref="NeighborhoodStreamer.TryRequestRegenerate"/>
    public bool TryRegenerate(int cellX, int cellY) => neighborhoodStreamer.TryRequestRegenerate(cellX, cellY);
}
