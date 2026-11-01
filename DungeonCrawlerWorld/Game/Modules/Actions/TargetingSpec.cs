using Engine.Tiles;

namespace Game.Modules.Actions;

/// <summary>
/// Defines the targetting parameters for an action.
/// </summary>
/// <remarks>
/// Every activation resolves in the same order: aim (is the aimed tile allowed), path (from the caster toward the aim),
/// anchor (where the shape goes), shape (its tiles), spread (which of those tiles the shape reaches past walls). Each
/// setting is independent, and a combination that would be redundant resolves the same as a simpler one.
///
/// AimLineOfSight and ShapeLineOfSight resolve over the map's walls (LineOfSight, World.BlocksSight). A Projectile or Lobbed
/// Path is traced with no travel time (PathTracer): its hits are ResolvedTargets.PathHitEntityIds, and a PathEnd anchor lays
/// the shape where it stopped.
/// </remarks>
/// <param name="Shape">The shape of the target area -- a [Flags] combination is valid (see TargetShape's own doc comment).</param>
/// <param name="MaximumRange">The farthest the aimed tile may be, as a rounded circle around the caster's footprint. A Placed shape anchored on the caster is aimed within its own area instead (AimRanges), so it leaves this 0.</param>
/// <param name="AreaSize">The shape's size: a Circle's radius, a Line's or Cone's length.</param>
/// <param name="Modes">Whether the activation can mark an entity (Target mode) or only aims at tiles (Ground mode).</param>
/// <param name="TargetModeAffects">What a Target-mode activation affects: the shape around the marked entity, or the marked entity alone.</param>
/// <param name="Anchor">Where the shape is laid out: where the path finished, or around the caster.</param>
/// <param name="MinimumRange">The nearest the aimed tile may be; with a Caster anchor, the shape's tiles nearer than this are left out. Never scaled.</param>
/// <param name="Path">How the effect gets from the caster to where it lands.</param>
/// <param name="PathEnd">Where a Projectile stops when nothing stops it earlier.</param>
/// <param name="ProjectileHitCount">How many entities a Projectile hits on its way, stopping on the last; AllEntitiesOnPath for every one.</param>
/// <param name="AimLineOfSight">Whether the caster must see the aimed tile to aim there.</param>
/// <param name="PathWalls">What a Projectile or Lobbed path does on reaching a wall.</param>
/// <param name="WallBounceCount">How many times a path that bounces off walls may bounce.</param>
/// <param name="ShapeLineOfSight">Whether each tile of the shape must be visible from its anchor.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record TargetingSpec(
    TargetShape Shape,
    int MaximumRange,
    int AreaSize = 0,
    TargetingModes Modes = TargetingModes.Both,
    TargetModeAffects TargetModeAffects = TargetModeAffects.Area,
    TargetAnchor Anchor = TargetAnchor.PathEnd,
    int MinimumRange = 0,
    TargetPath Path = TargetPath.Placed,
    ProjectilePathEnd PathEnd = ProjectilePathEnd.AtAimedTile,
    ushort ProjectileHitCount = 0,
    LineOfSightRule AimLineOfSight = LineOfSightRule.NotRequired,
    PathWallRule PathWalls = PathWallRule.StopsAtWall,
    byte WallBounceCount = 0,
    LineOfSightRule ShapeLineOfSight = LineOfSightRule.NotRequired)
{
    /// <summary>The ProjectileHitCount that is never reached: the projectile hits every entity on its way.</summary>
    public const ushort AllEntitiesOnPath = ushort.MaxValue;

    /// <summary>The shape resolves around the caster alone, whatever is aimed at: a Self shape anchored on the caster.</summary>
    public bool IsSelfOnly => Shape == TargetShape.Self && Anchor == TargetAnchor.Caster;

    /// <summary>The minimum and maximum range and area size as declared, unscaled.</summary>
    public TargetRanges Ranges => new(MaximumRange, AreaSize, MinimumRange);

    /// <summary>The ranges the aimed tile is held to (TargetShapeResolver.AimRanges): Ranges, except that a Placed shape anchored on the caster is aimed within its own area.</summary>
    public TargetRanges AimRanges => TargetShapeResolver.AimRanges(Anchor, Path, Ranges);
}

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
