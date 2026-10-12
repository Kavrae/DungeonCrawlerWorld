using Microsoft.Xna.Framework;

namespace Game.Terrain;

/// <summary>Everything every cell of one kind of terrain or structure shares: how it looks, what it's called, and what it does to whatever stands on or near it.</summary>
/// <remarks>
/// Terrain cells are not entities: a cell stores only a
/// TerrainCell -- this definition's registry id -- and everything else is read from here. That is
/// what keeps a 1024x1024x2 neighborhood of terrain to a few megabytes.
///
/// The same definitions fill two stores in Map: the floor beneath a MapLayer, and the structure
/// (a wall) standing on a MapLayer. Contact and Aura are read from floors only.
///
/// A terrain's sprite is its background: there is no colour under it. Terrain with no sprite draws
/// its glyph, if any, on the map's own flat fill, under its aura's glow.
/// </remarks>
/// <param name="Key">Stable, mod-safe identity. Runtime ids are assigned at registration and may differ between sessions; keys don't.</param>
/// <param name="SpriteName">The sprite manifest entry whose cells are this terrain's variants (a wall's face and top), or null for glyph-only terrain. Named only: what it draws as is Presentation's.</param>
/// <param name="Contact">What a cell of this terrain does to whatever steps onto, and stays on, it.</param>
/// <param name="Aura">The aura every cell of this terrain radiates, with its matching glow.</param>
/// <param name="BlocksMovement">No mover can enter a cell holding this, except a Phasing one.</param>
/// <param name="LinkGroup">Structures with the same group join each other (a door frame and the stone wall it sits in); null is the SpriteName, else the Key.</param>
public sealed record TerrainDefinition(
    string Key,
    string Name,
    string Description,
    string Glyph,
    Color GlyphColor,
    string? SpriteName = null,
    TerrainContact? Contact = null,
    TerrainAura? Aura = null,
    bool BlocksMovement = false,
    string? LinkGroup = null);
