namespace Game.Modules.ProcessingTier.Components;

/// <summary>
/// Nested region an entity currently falls into relative to the player, coarsest last -- see
/// ProcessingTierSystem's own doc comment for the exact boundaries. Doubles as the key into
/// each tier's cycle divisor (how many real frames pass between full processing for an entity
/// in that tier), so adding a coarser ring later is just another case, not a redesign.
/// </summary>
public enum ProcessingTierLevel : byte
{
    Local,
    Neighborhood,
    Borough,
    Beyond,
}
