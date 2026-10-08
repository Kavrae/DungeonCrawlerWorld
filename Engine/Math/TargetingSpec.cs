namespace Engine.Math;

/// <summary>
/// Defines the targetting parameters for an action.
/// </summary>
/// <param name="Shape">The shape of the target area -- a [Flags] combination is valid (see TargetShape's own doc comment).</param>
/// <param name="Range">The maximum distance from the caster to place the shape's anchor tile.</param>
/// <param name="AreaSize">The footprint radius at the anchor tile.</param>
/// <param name="Metric">Which grid-distance function Range is measured against for SingleTarget specifically (every other shape has its own fixed distance semantics). Manhattan by default, matching every SingleTarget caller before this field existed.</param>
/// <param name="Modes">Whether the activation can mark an entity (Target mode) or only aims at tiles (Ground mode).</param>
/// <param name="TargetModeAffects">What a Target-mode activation affects: the shape around the marked entity, or the marked entity alone.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record TargetingSpec(
    TargetShape Shape,
    int Range,
    int AreaSize = 0,
    DistanceMetric Metric = DistanceMetric.Manhattan,
    TargetingModes Modes = TargetingModes.Both,
    TargetModeAffects TargetModeAffects = TargetModeAffects.Area);

/// <summary>Which targeting modes an activation offers.</summary>
public enum TargetingModes : byte
{
    /// <summary>Target (mark the entity on the aimed tile, and follow it) or Ground (the aimed tiles), as the caster chooses.</summary>
    Both,

    /// <summary>Ground only: melee, Adjacent shapes, and anything whose tile is a destination rather than a target (Dodge).</summary>
    GroundOnly,

    /// <summary>Target only: the aimed entity, not the tiles it's on.</summary>
    TargetOnly
}

/// <summary>What a Target-mode activation affects.</summary>
public enum TargetModeAffects : byte
{
    /// <summary>The shape, centred on the marked entity's tile: everyone in it.</summary>
    Area,

    /// <summary>The marked entity alone (a potion thrown at someone).</summary>
    MarkedOnly,
}
