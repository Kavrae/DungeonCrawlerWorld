using Engine.Math;
using Microsoft.Xna.Framework;

namespace Game.Modules.Abilities;

/// <summary>
/// Shared, catalog-level ability data -- looked up by Id from AbilityCatalog. Composed from
/// three focused parts (Targeting/Timing/Effect) rather than one flat parameter list, so each
/// piece is independently reusable (e.g. several abilities sharing the same TargetingSpec
/// instance). Per-entity state (granted damage, remaining cooldown) lives on
/// AbilityInstanceComponent instead.
///
/// SpriteName/GlyphColor and Tags mirror Game.Modules.Inventory.ItemDefinition's own fields of
/// the same name/purpose -- see that record's doc comment for the sprite-first,
/// glyph-as-fallback convention HotbarContent.DrawAbilitySlot follows the same way DrawItemSlot
/// does. Trailing optional (SpriteName null, GlyphColor the Color default, Tags empty) rather
/// than positional alongside Glyph, so the many pre-existing test abilities built with only the
/// original six positional args (no sprite/tags of their own) didn't need to change.
///
/// Summary vs Description: Summary is a short, concrete statement of exact effect (states what
/// happens, not numerical magnitudes) meant to be read at a glance in a small window -- see
/// HotbarContent.TryGetSlotSummary, the Armed Hotkey Summary window's only consumer of it today.
/// Description is longer flavor/detail text for future, larger text boxes elsewhere -- the two
/// are deliberately separate fields, not one field reused for both purposes (see
/// Game.Modules.Inventory.ItemDefinition, which draws the same distinction).
/// </summary>
public sealed record AbilityDefinition(Guid Id, string Name, string Glyph, TargetingSpec Targeting, AbilityTiming Timing, AbilityEffect Effect, string Description = "", string Summary = "", string? SpriteName = null, Color GlyphColor = default, IReadOnlyList<Tag> Tags = null!)
{
    public IReadOnlyList<Tag> Tags { get; init; } = Tags ?? [];
}
