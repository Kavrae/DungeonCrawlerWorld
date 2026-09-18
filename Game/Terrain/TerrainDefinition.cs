using Game.Modules.Health.Components;
using Game.Modules.StatusEffectAura.Components;
using Microsoft.Xna.Framework;

namespace Game.Terrain;

/// <summary>Everything every cell of one kind of terrain or structure shares: how it looks, what it's called, and what it does to whatever stands on or near it.</summary>
/// <remarks>
/// Terrain cells are not entities: a cell stores only a
/// TerrainCell -- this definition's registry id plus a sprite variant -- and everything else is
/// read from here. That is what keeps a 1024x1024x2 neighborhood of terrain to a few megabytes.
///
/// The same definitions fill two stores in Map: the floor beneath a MapLayer, and the structure
/// (a wall) standing on a MapLayer. A structure's background replaces the floor's. ContactHazard
/// and Aura are read from floors only.
/// </remarks>
/// <param name="Key">Stable, mod-safe identity. Runtime ids are assigned at registration and may differ between sessions; keys don't.</param>
/// <param name="SpriteName">SpriteManifest entry whose cells are this terrain's variants, or null for glyph-only terrain.</param>
/// <param name="ContactHazard">Damage dealt to whatever steps onto, and stays on, a cell of this terrain.</param>
/// <param name="Aura">A status-effect aura every cell of this terrain radiates, with its matching glow.</param>
/// <param name="BlocksMovement">No mover can enter a cell holding this, except a Phasing one.</param>
public sealed record TerrainDefinition(
    string Key,
    string Name,
    string Description,
    Color BackgroundColor,
    string Glyph,
    Color GlyphColor,
    string? SpriteName = null,
    ContactHazard? ContactHazard = null,
    StatusEffectAuraSourceComponent? Aura = null,
    bool BlocksMovement = false);

/// <summary>Contact damage: DamagePerTick on arrival, then again every TickIntervalFrames while the occupant stays.</summary>
/// <param name="PreferredTargetType">The body part a hit lands on first; Bottommost when absent or missing.</param>
public readonly record struct ContactHazard(ushort DamagePerTick, ushort TickIntervalFrames, BodyPartType? PreferredTargetType = null);
