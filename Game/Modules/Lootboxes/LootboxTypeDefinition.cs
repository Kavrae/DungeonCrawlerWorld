using Microsoft.Xna.Framework;

namespace Game.Modules.Lootboxes;

/// <summary>A kind of loot box, such as Adventurer or Alchemist, which every rarity of that box shares.</summary>
/// <param name="Id">The type's identity; a mod replaces a built-in type by registering the same Id.</param>
/// <param name="Name">The type's name as a box shows it: "Adventurer" in "Bronze Adventurer Box".</param>
/// <param name="SpriteName">The base of the sprite names this type's boxes show, one per rarity ("{SpriteName}-{Rarity}", see LootboxCatalog.SpriteNameFor); null for the default loot box base.</param>
/// <param name="Glyph">The fallback glyph; null for the default loot box glyph.</param>
/// <param name="GlyphColor">The fallback glyph's color, the same for every rarity; null for the default loot box glyph color.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record LootboxTypeDefinition(Guid Id, string Name, string? SpriteName = null, string? Glyph = null, Color? GlyphColor = null);
