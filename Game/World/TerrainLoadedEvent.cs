using Engine.Math;
using Game.Terrain;

namespace Game.World;

/// <summary>Published by NeighborhoodStreamer as a loaded neighborhood's terrain and walls are announced: one area, a row at a time, before its population is created.</summary>
/// <remarks>
/// Consumers: the AuraField, which adds
/// AuraCells -- before the population, so a creature spawned beside lava is exposed straight away --
/// and MapWindow's cached terrain image when the area is on screen. AuraCells is every aura-radiating
/// terrain cell in Area, listed when the neighborhood was planned (TerrainAuraSources.ByRow), so no
/// consumer scans the area for them. A row at a time so the splatting spreads over frames. Startup
/// population publishes nothing: the field scans the whole map when it is built.
/// </remarks>
public readonly record struct TerrainLoadedEvent(MapBounds Area, IReadOnlyList<TerrainAuraCell> AuraCells);
