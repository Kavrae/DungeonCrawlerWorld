namespace Engine.Math;

/// <summary>
/// Range and AreaSize are independent knobs, not redundant: Range is how far from the caster
/// the shape's anchor tile may be placed (the cursor tile for SingleTarget/Burst/Line/Cone --
/// irrelevant for Adjacent, which is always anchored on the caster itself). AreaSize is the
/// footprint radius at that anchor -- only Burst (and Adjacent, which is really a self-anchored
/// Burst -- see TargetShapeResolver) use it as a genuinely separate dimension from Range; a
/// Line or Cone's one meaningful number is how far it reaches, which Range alone already
/// covers, so AreaSize stays unused (0) for those two. Defaults to 0 so shapes that don't use
/// it (SingleTarget, Line, Cone) don't need to say so at every call site.
///
/// Shared, not Abilities-specific -- Game.Modules.Abilities.AbilityDefinition.Targeting and
/// Game.Modules.Inventory.ConsumableEffect.Targeting (thrown/used potions, splash included)
/// both reference this same type, since neither the shape/range/area tuple nor
/// TargetShapeResolver's actual algorithm has any Ability-specific knowledge to begin with.
/// </summary>
public sealed record TargetingSpec(TargetShape Shape, int Range, int AreaSize = 0);
