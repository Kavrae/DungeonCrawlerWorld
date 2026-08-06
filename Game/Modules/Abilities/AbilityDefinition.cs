using Engine.Math;

namespace Game.Modules.Abilities;

/// <summary>
/// Shared, catalog-level ability data -- looked up by Id from AbilityCatalog. Composed from
/// three focused parts (Targeting/Timing/Effect) rather than one flat parameter list, so each
/// piece is independently reusable (e.g. several abilities sharing the same TargetingSpec
/// instance). Per-entity state (granted damage, remaining cooldown) lives on
/// AbilityInstanceComponent instead.
///
/// Summary vs Description: Summary is a short, concrete statement of exact effect (states what
/// happens, not numerical magnitudes) meant to be read at a glance in a small window -- see
/// HotbarContent.TryGetSlotSummary, the Armed Hotkey Summary window's only consumer of it today.
/// Description is longer flavor/detail text for future, larger text boxes elsewhere -- the two
/// are deliberately separate fields, not one field reused for both purposes (see
/// Game.Modules.Inventory.ItemDefinition, which draws the same distinction).
/// </summary>
public sealed record AbilityDefinition(Guid Id, string Name, string Glyph, TargetingSpec Targeting, AbilityTiming Timing, AbilityEffect Effect, string Description = "", string Summary = "");
