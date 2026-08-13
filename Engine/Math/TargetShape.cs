namespace Engine.Math;

/// <summary>
/// The 2d footprint an ability's targeting resolves to.
/// </summary>
/// <remarks
/// This shape is shared between Game (hit resolution) and Presentation (tile highlighting).
/// 
/// See TargetShapeResolver for the actual algorithm that calculates the tiles in a given shape, and TargetingSpec for the parameters that define how to use it.
/// </remarks>
public enum TargetShape
{
    /// <summary>
    /// The perimeter ring of tiles surrounding the caster's own footprint.
    /// </summary>
    /// <remarks>
    /// Chebyshev distance &lt;= 1 from any footprint tile.
    /// Deliberately excludes the caster's own footprint.
    /// 
    /// TargetingSpec.AreaSize and Range are not valid for this shape.
    /// 
    ///This is the melee default.
    ///</remarks>
    Adjacent,

    /// <summary>
    /// The perimeter ring of tiles surrounding the caster's own footprint, plus the caster's own
    /// footprint tiles themselves.
    /// </summary>
    /// <remarks>
    /// Same ring as Adjacent (Chebyshev distance &lt;= 1 from any footprint tile), but does not
    /// exclude the caster's own footprint -- for effects meant to be valid on the caster too, not
    /// just whoever/whatever is standing next to them (e.g. a self-or-adjacent healing scroll).
    ///
    /// TargetingSpec.AreaSize and Range are not valid for this shape.
    /// </remarks>
    AdjacentWithSelf,

    /// <summary>
    /// A single tile at the cursor.
    /// </summary>
    /// <remarks>
    /// Valid within a set distance as defined by the TargetingSpec.Range parameter.
    /// TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    SingleTarget,

    /// <summary>
    /// A straight line of tiles from the caster through the cursor.
    /// </summary>
    /// <remarks>
    /// Any angle, not snapped to a fixed set of directions.
    /// 
    /// The length is set distance as defined by the TargetingSpec.Range parameter.
    ///  TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    Line,

    /// <summary>
    /// A cone of tiles from the caster through the cursor.
    /// </summary>
    /// <remarks>
    /// Tiles within Range whose angle from the caster-to-cursor direction falls within a fixed threshold.
    /// 
    /// The range is defined by the TargetingSpec.Range parameter.
    /// TargetingSpec.AreaSize is not valid for this shape.
    /// </remarks>
    Cone,

    /// <summary>
    /// A diamond-shaped area.
    /// </summary>
    /// <remarks>
    /// The range of the shape's center file is defined by the TargetingSpec.Range parameter.
    /// The shape's area can extend past the range limit, but the center tile must be within range.
    /// The size of the shape is defined by the TargetingSpec.AreaSize parameter.
    /// </remarks>
    Burst,

    /// <summary>
    /// The caster's own tiles
    /// </summary>
    /// <remarks>
    /// TargetingSpec.Range and AreaShape are not valid for this shape.
    /// </remarks>
    Self
}
