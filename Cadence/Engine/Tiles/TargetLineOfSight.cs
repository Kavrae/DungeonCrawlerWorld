namespace Engine.Tiles;

/// <summary>The line-of-sight rules a targeting resolve applies, and the walls it applies them over (LineOfSight).</summary>
/// <remarks>The default -- no walls -- applies neither rule, so everything reaches through walls and can be aimed into one as before walls were modelled. With walls, nothing is ever aimed at or placed inside one (PlacementWalls), whichever rules apply.</remarks>
/// <param name="Walls">The tiles that block sight; null applies no rule.</param>
/// <param name="Aim">Whether the caster must see the aimed tile to aim there.</param>
/// <param name="Shape">Whether each tile of the shape must be visible from its anchor.</param>
public readonly record struct TargetLineOfSight(ISightBlockers? Walls, LineOfSightRule Aim = LineOfSightRule.NotRequired, LineOfSightRule Shape = LineOfSightRule.NotRequired)
{
    /// <summary>Walls to aim past, when the aim needs line of sight; else null.</summary>
    public ISightBlockers? AimWalls => Aim == LineOfSightRule.Required ? Walls : null;

    /// <summary>Walls a shape spreads past, when it needs line of sight from its anchor; else null.</summary>
    public ISightBlockers? ShapeWalls => Shape == LineOfSightRule.Required ? Walls : null;

    /// <summary>Walls nothing can be aimed at or placed inside, whatever the rules: Walls.</summary>
    /// <remarks>A wall never hides its own tile, so a shape centred inside one would see out of both sides of it.</remarks>
    public ISightBlockers? PlacementWalls => Walls;
}
