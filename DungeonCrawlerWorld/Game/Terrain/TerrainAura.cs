namespace Game.Terrain;

/// <summary>The aura every cell of a terrain radiates.</summary>
/// <param name="AuraId">The aura's Guid in the content's Auras table, resolved through the session's AuraCatalog.</param>
/// <param name="Power">Each cell's value at its own tile (see AuraSourceComponent).</param>
/// <param name="Size">How many tiles each cell reaches.</param>
public readonly record struct TerrainAura(Guid AuraId, ushort Power, byte Size);
