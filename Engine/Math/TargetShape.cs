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
    /// <summary>The perimeter ring of tiles surrounding the caster's own footprint (Chebyshev distance &lt;= 1 from any footprint tile) -- melee default. Deliberately excludes the caster's own footprint entirely, even for a Phasing/Tiny entity sharing one of those tiles -- an entity hugging the caster's own tile(s) is meant to be a real, hard-to-deal-with melee threat, not an automatic target.</summary>
    Adjacent,

    /// <summary>A single tile at the cursor, valid only within Range of the caster -- e.g. a ranged single-target attack.</summary>
    SingleTarget,

    /// <summary>A straight line of Range tiles from the caster through any point the cursor is aimed at -- any angle, not snapped to a fixed set of directions.</summary>
    Line,

    /// <summary>Tiles within Range whose angle from the caster-to-cursor direction falls within a fixed threshold.</summary>
    Cone,

    /// <summary>A diamond-shaped area of Range tiles centered on the cursor.</summary>
    Burst,

    /// <summary>The caster's own tile only -- no cursor/range involved. For no-target self-cast abilities (e.g. a self-buff); ignores whatever tile was clicked/hovered, the same way Adjacent ignores it.</summary>
    Self
}
