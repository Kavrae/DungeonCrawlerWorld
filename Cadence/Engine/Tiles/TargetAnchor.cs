namespace Engine.Tiles;

/// <summary>Where a targeting shape's tiles are laid out.</summary>
/// <remarks>Directional shapes (Line, Cone) always face from the caster toward the aimed tile, whichever the anchor.</remarks>
public enum TargetAnchor : byte
{
    /// <summary>Wherever the path finished: for an effect placed with no path, the aimed tile.</summary>
    /// <remarks>
    /// A directional shape starts at the end tile, includes it, and extends AreaSize past it, away from the caster. A
    /// non-directional shape surrounds the end tile. The aimed tile must lie within the range band.
    /// </remarks>
    PathEnd,

    /// <summary>The caster's own footprint, wherever the path went.</summary>
    /// <remarks>
    /// The caster's footprint is left out of every shape except Self, and so is every tile nearer than the minimum range.
    /// With no path, the aim gives only a direction, within the shape's own reach (TargetShapeResolver.AimRanges).
    /// </remarks>
    Caster,
}
