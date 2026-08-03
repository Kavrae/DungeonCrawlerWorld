namespace Engine.Math;

/// <summary>
/// The footprint an ability's targeting resolves to -- shared vocabulary between Game (hit
/// resolution) and Presentation (tile highlighting); see TargetShapeResolver for the actual
/// algorithm, kept here alongside it for the same layering reason DistanceFalloff is Engine-side
/// rather than Game-side (both Game and Presentation depend downward on it, instead of
/// Presentation depending sideways on a Game-layer algorithm).
/// </summary>
public enum TargetShape
{
    /// <summary>The caster's own tile plus its 8 surrounding neighbors (Chebyshev distance &lt;= 1) -- melee default. Includes the caster's own tile so a Phasing/Tiny entity sharing it is still a valid target.</summary>
    Adjacent,

    /// <summary>A single tile at the cursor, valid only within Range of the caster -- e.g. a ranged single-target attack.</summary>
    SingleTarget,

    /// <summary>A straight line of Range tiles from the caster toward the cursor, along whichever of the 8 cardinal/diagonal directions is nearest the cursor direction.</summary>
    Line,

    /// <summary>Tiles within Range whose angle from the caster-to-cursor direction falls within a fixed threshold.</summary>
    Cone,

    /// <summary>A diamond-shaped area of Range tiles centered on the cursor.</summary>
    Burst,

    /// <summary>The caster's own tile only -- no cursor/range involved. For no-target self-cast abilities (e.g. a self-buff); ignores whatever tile was clicked/hovered, the same way Adjacent ignores it.</summary>
    Self
}
