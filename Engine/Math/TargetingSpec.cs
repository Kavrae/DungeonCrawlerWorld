namespace Engine.Math;

/// <summary>
/// Defines the targetting parameters for an action.
/// </summary>
/// <param name="Shape">The shape of the target area.</param>
/// <param name="Range">The maximum distance from the caster to place the shape's anchor tile.</param>
/// <param name="AreaSize">The footprint radius at the anchor tile.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record TargetingSpec(TargetShape Shape, int Range, int AreaSize = 0);
