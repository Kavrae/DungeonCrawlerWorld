using Game.Modules;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>
/// Shared, catalog-level item data -- looked up by Id from ItemCatalog. Same abstraction level as
/// Game.Modules.Actions.ActionDefinition (both derive from ActivatableDefinition) except for
/// MaxStackSize/Activator's nullability -- an Equipment/Tool item legitimately has neither.
///
/// MaxStackSize null means unbounded (every item predating this field). Not yet enforced by
/// InventoryActions.AddItem -- nothing grants more than a handful of any one item today, so
/// there's no real call site to wire the clamp through yet (see TODO.md's Inventory system note
/// on not designing storage-divergence machinery before something actually needs it). Activator
/// is null for anything that isn't usable (e.g. an Equipment/Tool item) -- typed IActionActivator
/// rather than a Potion-specific type so a future ScrollActivator/WandActivator slots into this
/// same field with zero ItemDefinition changes; ConsumableActivationSystem is what actually
/// interprets a Game.Modules.Actions.Activators.PotionActivator specifically today.
/// </summary>
public sealed record ItemDefinition(
    Guid Id,
    string Name,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<ActionEffect> Effects,
    string Description = "",
    string Summary = "",
    int? MaxStackSize = null,
    IActionActivator? Activator = null)
    : ActivatableDefinition(Id, Name, SpriteName, Glyph, GlyphColor, Tags, Effects, Description, Summary);
