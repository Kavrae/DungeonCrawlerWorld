using Game.Modules.Auras;

namespace Game.Terrain;

/// <summary>The aura every cell of a terrain radiates.</summary>
/// <param name="Aura">The aura's definition, declared with the terrain that radiates it.</param>
/// <param name="Power">Each cell's value at its own tile (see AuraSourceComponent).</param>
/// <param name="Size">How many tiles each cell reaches.</param>
public readonly record struct TerrainAura(AuraDefinition Aura, ushort Power, byte Size);
