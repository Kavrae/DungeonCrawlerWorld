namespace Engine.Math;

/// <summary>
/// The 2d footprint on the map tiles that an ability's targeting resolves to. A [Flags] enum, not
/// a plain one -- shapes compose by union (e.g. Adjacent | Self replaces the old dedicated
/// AdjacentWithSelf value; a future Cone | Adjacent could add a caster-centered ring to a cone
/// attack) rather than needing a new named value minted for every useful combination. See
/// TargetShapeResolver.Resolve's own doc comment for exactly how combined flags resolve (each
/// bit's own tiles unioned, with a couple of provably-redundant combinations short-circuited for
/// performance) and its important anchor-sharing caveat (every flag in one combined resolve shares
/// the same cursorTile/range/areaSize -- there's no per-flag independent anchor).
/// </summary>
/// <remarks
/// This shape is shared between Game (hit resolution) and Presentation (tile highlighting).
///
/// See TargetShapeResolver for the actual algorithm that calculates the tiles in a given shape, and TargetingSpec for the parameters that define how to use it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
[Flags]
public enum TargetShape : byte
{
    /// <summary> The perimeter ring of tiles surrounding the caster's own footprint. </summary>
    /// <remarks>
    /// Chebyshev distance &lt;= 1 from any footprint tile.
    /// Deliberately excludes the caster's own footprint.
    ///
    /// TargetingSpec.AreaSize and Range are not valid for this shape.
    ///</remarks>
    Adjacent = 1 << 0,

    /// <summary> The caster's own footprint tiles. </summary>
    /// <remarks>
    /// Combine with Adjacent (Adjacent | Self) for the old AdjacentWithSelf shape: the same ring
    /// plus the caster's own footprint tiles.
    ///
    /// TargetingSpec.Range and AreaSize are not valid for this shape.
    /// </remarks>
    Self = 1 << 1,

    /// <summary> A single tile at the cursor. </summary>
    /// <remarks>
    /// Valid within a set distance -- TargetingSpec.Range under TargetingSpec.Metric (Manhattan by
    /// default; Chebyshev for a "one tile out of the 3x3 block around the caster" pick, e.g. Dodge).
    ///
    /// TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    SingleTarget = 1 << 2,

    /// <summary> A straight line of tiles from the caster through the cursor. </summary>
    /// <remarks>
    /// Tiles within Range that fall along the line from the caster to the cursor.
    ///
    /// The length is set distance as defined by the TargetingSpec.Range parameter.
    /// TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    Line = 1 << 3,

    /// <summary> A cone of tiles from the caster through the cursor. </summary>
    /// <remarks>
    /// Tiles within Chebyshev distance Range whose angle from the caster-to-cursor direction falls
    /// within a fixed threshold.
    ///
    /// The range is defined by the TargetingSpec.Range parameter.
    /// TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    Cone = 1 << 4,

    /// <summary> A diamond-shaped area. </summary>
    /// <remarks>
    /// The range of the shape's center file is defined by the TargetingSpec.Range parameter.
    /// The shape's area can extend past the range limit, but the center tile must be within range.
    /// The size of the shape is defined by the TargetingSpec.AreaSize parameter.
    /// </remarks>
    Burst = 1 << 5,
}
