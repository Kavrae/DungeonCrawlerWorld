using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>
/// Shared, catalog-level item data -- looked up by Id from ItemCatalog. Glyph/GlyphColor is the
/// fallback drawn when SpriteName is null or has no SpriteManifest entry -- sprite-first,
/// glyph-as-fallback is implied by SpriteName's presence, not restated in the field names (see
/// GlyphComponent, which names its own fields the same way). Per-inventory-slot state
/// (quantity, disabled) lives on InventoryItemStackComponent instead.
///
/// MaxStackSize null means unbounded (every item predating this field). Not yet enforced by
/// InventoryActions.AddItem -- nothing grants more than a handful of any one item today, so
/// there's no real call site to wire the clamp through yet (see TODO.md's Inventory system note
/// on not designing storage-divergence machinery before something actually needs it). Consumable
/// is null for anything that isn't usable/consumable (e.g. the Hammer, an Equipment/Tool item) --
/// ConsumableActivationSystem is what actually interprets it.
/// </summary>
public sealed record ItemDefinition(Guid Id, string Name, string? SpriteName, string Glyph, Color GlyphColor, IReadOnlyList<string> Tags, string Description = "", int? MaxStackSize = null, ConsumableEffect? Consumable = null);
