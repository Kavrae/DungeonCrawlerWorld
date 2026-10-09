using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>A sprite to draw: a cell of a spritesheet.</summary>
public readonly record struct SpriteView(string SheetPath, Rectangle SourceRectangle);

/// <summary>How an entity looks on the map: its sprite if it has one, otherwise its glyph, plus whether it is a corpse.</summary>
/// <remarks>A sprite always wins when set. Glyph may still be set beside it -- a blueprint's appearance declares both -- for a reader that only ever draws glyphs; it is empty when the sprite is a per-instance SpriteComponent.</remarks>
public readonly record struct EntityVisualView(SpriteView? Sprite, string Glyph, Color GlyphColor, bool IsDead);

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
/// <param name="IsContainer">Lootable while alive, so shows its loot bag without being a corpse.</param>
/// <param name="LootBag">The badge a container or corpse carrying items shows; None for anything else.</param>
/// <param name="IsDodging">Inside a Dodge immunity window.</param>
public readonly record struct EntityStatusView(float? HealthFraction, bool IsContainer, LootBagState LootBag, bool IsDodging);

/// <summary>The action an entity is winding up, for the charging badge above it.</summary>
public readonly record struct ChargingActionView(SpriteView? Sprite, string Glyph, Color GlyphColor);

/// <summary>A cell's terrain, as the map and the inspector show it.</summary>
public readonly record struct TerrainView(string Name, string Description, EntityVisualView Visual);

/// <summary>What the map's right-click menu needs to know about one thing on a tile.</summary>
/// <param name="IsDestroyed">Dead -- a destroyed shop or container keeps its components, so this, not IsShop, decides whether it still trades.</param>
/// <param name="IsLootReservedFromPlayer">A corpse whose loot belongs to someone else for now (LootRights): the player may not open it yet.</param>
public readonly record struct EntityInteractionView(string Name, bool IsShop, bool IsContainer, bool IsDestroyed, bool IsLootReservedFromPlayer);
