using Engine.Math;
using Game.Modules.Auras.Components;

namespace Game.Terrain;

/// <summary>One terrain cell that radiates an aura: where it is, and the aura.</summary>
public readonly record struct TerrainAuraCell(Vector3Int Position, AuraSourceComponent Aura);
