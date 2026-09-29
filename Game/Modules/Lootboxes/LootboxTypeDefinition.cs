namespace Game.Modules.Lootboxes;

/// <summary>A kind of loot box, such as Adventurer or Alchemist, which every rarity of that box shares.</summary>
/// <param name="Id">The type's identity; a mod replaces a built-in type by registering the same Id.</param>
/// <param name="Name">The type's name as a box shows it: "Adventurer" in "Bronze Adventurer Box".</param>
/// <param name="SpriteName">The sprite every box of this type shows; null for the default loot box sprite.</param>
/// <param name="Glyph">The fallback glyph; null for the default loot box glyph.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record LootboxTypeDefinition(Guid Id, string Name, string? SpriteName = null, string? Glyph = null);
