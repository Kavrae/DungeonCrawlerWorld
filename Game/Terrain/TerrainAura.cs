using Game.Modules.Auras;

namespace Game.Terrain;

/// <summary>The aura every cell of a terrain radiates.</summary>
/// <param name="Aura">The aura's definition, declared with the terrain that radiates it.</param>
/// <param name="Strength">Each cell's strength, which also sets its reach (see AuraSourceComponent).</param>
public readonly record struct TerrainAura(AuraDefinition Aura, byte Strength);
