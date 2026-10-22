using Game.Modules.Core.Components;

namespace Game.Areas;

/// <summary>A named tile of an area that means something to the game -- where the player starts.</summary>
/// <param name="MarkerId">Unique within the area.</param>
public sealed record AreaMarker(string MarkerId, int X, int Y, AreaMarkerKind Kind, MapLayer Layer = MapLayer.Ground);

/// <summary>What a marker means.</summary>
public enum AreaMarkerKind : byte
{
    /// <summary>Not a kind: a marker must name one.</summary>
    None = 0,

    /// <summary>Where the player is spawned; exactly one across the fixed placements.</summary>
    PlayerSpawn = 1,
}
