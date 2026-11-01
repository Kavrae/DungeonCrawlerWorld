using Engine.Math;

namespace Engine.Tiles;

/// <summary>The three distances a targeting shape resolves with, each a rounded circle (GridDistance.IsWithinRoundedRadius) from the caster's nearest footprint tile, or from the anchor for AreaSize.</summary>
/// <param name="MaximumRange">The farthest the aimed tile may be.</param>
/// <param name="AreaSize">The shape's size: a Circle's radius, a Line's or Cone's length.</param>
/// <param name="MinimumRange">The nearest the aimed tile may be; with TargetAnchor.Caster, the shape's tiles nearer than this are left out. 0 is no minimum.</param>
public readonly record struct TargetRanges(int MaximumRange, int AreaSize = 0, int MinimumRange = 0)
{
    /// <summary>Whether tile lies in the band from MinimumRange to MaximumRange of the footprint at origin.</summary>
    public bool IsInRangeBand(Vector3Int origin, Vector2Byte originSize, Vector3Int tile) =>
        GridDistance.IsWithinRoundedRadius(origin, originSize, tile, MaximumRange) && !IsNearerThanMinimum(origin, originSize, tile);

    /// <summary>Whether tile is nearer to the footprint at origin than MinimumRange.</summary>
    public bool IsNearerThanMinimum(Vector3Int origin, Vector2Byte originSize, Vector3Int tile) =>
        GridDistance.IsWithinRoundedRadius(origin, originSize, tile, MinimumRange - 1);
}
