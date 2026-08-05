using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>
/// Shared, catalog-level item data -- looked up by Id from ItemCatalog. Glyph/GlyphColor is the
/// fallback drawn when SpriteName is null or has no SpriteManifest entry -- sprite-first,
/// glyph-as-fallback is implied by SpriteName's presence, not restated in the field names (see
/// GlyphComponent, which names its own fields the same way). Per-inventory-slot state
/// (quantity, disabled) lives on InventoryItemStackComponent instead.
/// </summary>
public sealed record ItemDefinition(Guid Id, string Name, string? SpriteName, string Glyph, Color GlyphColor, IReadOnlyList<string> Tags);
