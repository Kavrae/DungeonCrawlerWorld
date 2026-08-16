namespace Engine.Math;

/// <summary>How DistanceFalloff computes a visited cell's contribution.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum FalloffShape : byte
{
    /// <summary>Contribution halves per tile of distance from the source, via DistanceFalloff.ValueAtDistance -- the classic radiating-aura shape.</summary>
    Fading,

    /// <summary>Every visited cell gets the full, undecayed strength.</summary>
    Flat,
}
