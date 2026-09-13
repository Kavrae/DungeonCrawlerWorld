namespace Engine.Math;

/// <summary>
/// Defines the targetting parameters for an action.
/// </summary>
/// <param name="Shape">The shape of the target area -- a [Flags] combination is valid (see TargetShape's own doc comment).</param>
/// <param name="Range">The maximum distance from the caster to place the shape's anchor tile.</param>
/// <param name="AreaSize">The footprint radius at the anchor tile.</param>
/// <param name="Metric">Which grid-distance function Range is measured against for SingleTarget specifically (every other shape has its own fixed distance semantics). Manhattan by default, matching every SingleTarget caller before this field existed.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record TargetingSpec(TargetShape Shape, int Range, int AreaSize = 0, DistanceMetric Metric = DistanceMetric.Manhattan);
