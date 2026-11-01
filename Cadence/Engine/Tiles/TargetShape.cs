namespace Engine.Tiles;

/// <summary>
/// The 2D footprint on the map's tiles that a targeting shape resolves to, laid out around its anchor (TargetAnchor). A
/// [Flags] enum, not a plain one -- shapes compose by union (e.g. Adjacent | Self is a ring and its centre) rather than
/// needing a new named value minted for every useful combination. Every flag in one combined resolve shares the same
/// anchor, aim and ranges; see TargetShapeResolver.Resolve.
/// </summary>
/// <remarks>
/// This shape is shared between Game (hit resolution) and Presentation (tile highlighting).
///
/// See TargetShapeResolver for the actual algorithm that calculates the tiles in a given shape. Its size is
/// TargetRanges.AreaSize, a rounded circle (GridDistance.IsWithinRoundedRadius) around the anchor. Directional shapes (Line,
/// Cone, Wall, Sweep) face from the caster toward the aimed tile.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
[Flags]
public enum TargetShape : ushort
{
    /// <summary> No tiles: only what the path hits. </summary>
    None = 0,

    /// <summary> The perimeter ring of tiles surrounding the anchor's footprint. </summary>
    /// <remarks>
    /// Chebyshev distance &lt;= 1 from any footprint tile, the footprint itself excluded: at TargetAnchor.Caster the caster's,
    /// at TargetAnchor.PathEnd the end tile.
    ///
    /// The area size is not valid for this shape.
    ///</remarks>
    Adjacent = 1 << 0,

    /// <summary> The caster's own footprint, at TargetAnchor.Caster. </summary>
    /// <remarks>
    /// Combine with Adjacent (Adjacent | Self) for the ring plus its centre. At TargetAnchor.PathEnd it covers nothing: the
    /// tile there is SingleTarget.
    ///
    /// The area size is not valid for this shape.
    /// </remarks>
    Self = 1 << 1,

    /// <summary> The end tile, at TargetAnchor.PathEnd: the aimed tile, or where the path stopped. </summary>
    /// <remarks>
    /// Combine with Adjacent (Adjacent | SingleTarget) for the ring plus its centre. At TargetAnchor.Caster it covers
    /// nothing: the caster's own tiles are Self.
    ///
    /// The area size is not valid for this shape.
    /// </remarks>
    SingleTarget = 1 << 2,

    /// <summary> A straight line of tiles along the caster-to-aim direction. </summary>
    /// <remarks>
    /// At TargetAnchor.Caster, from the caster's footprint tile nearest the aim, through the aim; at TargetAnchor.PathEnd,
    /// the same line from the end tile onward. Its length is the area size.
    /// </remarks>
    Line = 1 << 3,

    /// <summary> A cone of tiles along the caster-to-aim direction. </summary>
    /// <remarks>
    /// Tiles whose angle from the caster-to-aim direction is at most arctan(1/2) -- the tabletop cone, as wide at each
    /// distance as that distance (about 53 degrees in all) -- so its far edge is an arc. Its apex is the caster's footprint
    /// tile nearest the aim at TargetAnchor.Caster, the end tile at TargetAnchor.PathEnd; its length is the area size.
    /// </remarks>
    Cone = 1 << 4,

    /// <summary> A rounded circle of tiles around the anchor. </summary>
    /// <remarks>
    /// Its radius is the area size: around the end tile at TargetAnchor.PathEnd, around the caster's footprint (which is
    /// left out) at TargetAnchor.Caster. It can reach past the maximum range, which limits only the aim.
    /// </remarks>
    Circle = 1 << 5,

    /// <summary> The circle's outermost band, one tile thick, around the anchor. </summary>
    /// <remarks>
    /// The tiles within the area size of the anchor's footprint but not within one less: around the end tile at
    /// TargetAnchor.PathEnd, around the caster's footprint at TargetAnchor.Caster.
    /// </remarks>
    Ring = 1 << 6,

    /// <summary> A line one tile thick across the caster-to-aim direction, centred on the anchor tile. </summary>
    /// <remarks>
    /// Its length in tiles is the area size; an odd length is centred exactly, an even one has its extra tile on one side
    /// of the direction. Across a diagonal aim it is a diagonal line of tiles touching at corners. Its anchor tile is the
    /// end tile at TargetAnchor.PathEnd, the caster's footprint tile nearest the aim at TargetAnchor.Caster.
    /// </remarks>
    Wall = 1 << 7,

    /// <summary> A square centred on the anchor. </summary>
    /// <remarks>Every tile within Chebyshev distance of the area size (its half-side) from the anchor's footprint.</remarks>
    Square = 1 << 8,

    /// <summary> A plus sign centred on the anchor. </summary>
    /// <remarks>The tiles in the anchor footprint's own rows and columns, out to the area size (its arm length).</remarks>
    Cross = 1 << 9,

    /// <summary> The adjacent tiles on the facing side of the anchor's footprint: three for a 1x1. </summary>
    /// <remarks>
    /// The Adjacent ring's tiles that lie in the eighth of a turn nearest the caster-to-aim direction or either eighth beside
    /// it. The area size is not valid for this shape.
    /// </remarks>
    Sweep = 1 << 10,
}
