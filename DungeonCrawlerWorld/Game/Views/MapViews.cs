using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>How an entity looks on the map: the sprite it is named by and the seed its variant is picked from, its glyph, and whether it is a corpse.</summary>
/// <remarks>A sprite always wins when the manifest has one by that name; the glyph is drawn otherwise. Which cell the name and seed mean is Presentation's to decide.</remarks>
/// <param name="SpriteName">The sprite manifest entry, or null for glyph only.</param>
/// <param name="SpriteSeed">The seed the entry's variant is picked from: the spawn record's, or a per-instance override's own.</param>
public readonly record struct EntityVisualView(string? SpriteName, uint SpriteSeed, string Glyph, Color GlyphColor, bool IsDead);

/// <summary>Where an occupant sits and how the map draws it relative to the tile's other occupants.</summary>
/// <param name="Position">The occupant's origin tile -- a multi-tile occupant is drawn once, from here.</param>
/// <param name="Size">Footprint in tiles.</param>
/// <param name="Kind">Every non-blocking exemption it currently holds, combined.</param>
public readonly record struct OccupantView(Vector3Int Position, Vector2Byte Size, NonBlockingKind Kind, bool IsDead);

/// <summary>Whether an entity shows the loot-bag badge, and in which tint.</summary>
public enum LootBagState : byte
{
    None,
    Unlooted,
    Looted,
}

/// <summary>The per-entity status cues the map draws over an occupant.</summary>
/// <param name="HealthFraction">Current over modifier-effective maximum health, or null when the bar is hidden (no health, or full).</param>
/// <param name="IsContainer">Lootable while alive (a container that isn't a standing shop), so shows its loot bag without being a corpse.</param>
/// <param name="LootBag">The badge a container or corpse carrying items shows; None for anything else.</param>
/// <param name="IsDodging">Inside a Dodge immunity window.</param>
public readonly record struct EntityStatusView(float? HealthFraction, bool IsContainer, LootBagState LootBag, bool IsDodging);

/// <summary>The action an entity is winding up, for the charging badge above it.</summary>
/// <param name="SpriteName">The action or item's icon (its first cell), or null for glyph only.</param>
public readonly record struct ChargingActionView(string? SpriteName, string Glyph, Color GlyphColor);

/// <summary>How a cell's terrain or structure looks: its sprite name and the cell's seed (CellSeeds), and its glyph for when there is no sprite.</summary>
public readonly record struct TerrainVisualView(string? SpriteName, uint Seed, string Glyph, Color GlyphColor);

/// <summary>A cell's terrain or structure, as the inspector and the context menu show it.</summary>
public readonly record struct TerrainView(string Name, string Description, TerrainVisualView Visual);

/// <summary>What the map's right-click menu needs to know about one thing on a tile.</summary>
/// <param name="IsDestroyed">Dead -- a destroyed shop or container keeps its components, so this, not IsShop, decides whether it still trades.</param>
/// <param name="IsLootReservedFromPlayer">A corpse whose loot belongs to someone else for now (LootRights): the player may not open it yet.</param>
public readonly record struct EntityInteractionView(string Name, bool IsShop, bool IsContainer, bool IsDestroyed, bool IsLootReservedFromPlayer);
