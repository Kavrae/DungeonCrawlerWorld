namespace Game.Modules.Abilities;

/// <summary>
/// Shared, catalog-level ability data -- looked up by Id from AbilityCatalog. Composed from
/// three focused parts (Targeting/Timing/Effect) rather than one flat parameter list, so each
/// piece is independently reusable (e.g. several abilities sharing the same AbilityTargeting
/// instance). Per-entity state (granted damage, remaining cooldown) lives on
/// AbilityInstanceComponent instead.
/// </summary>
public sealed record AbilityDefinition(Guid Id, string Name, string Glyph, AbilityTargeting Targeting, AbilityTiming Timing, AbilityEffect Effect);
