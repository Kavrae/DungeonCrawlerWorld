using Engine.Math;

namespace Game.World;

/// <summary>Published by NeighborhoodStreamer as a loading neighborhood's terrain and walls are written: one area, a row at a time, before its population is created.</summary>
/// <remarks>
/// Consumers: the aura grids on both sides (StatusEffectAuraSystem, MapTintGrid), which splat the
/// area's terrain auras -- before the population, so a creature spawned beside lava is exposed
/// straight away -- and MapWindow's cached terrain image when the area is on screen. A row at a time
/// so the scan spreads over frames with the writing. Startup population publishes nothing: the grids
/// scan the whole map when they build.
/// </remarks>
public readonly record struct TerrainLoadedEvent(MapBounds Area);
