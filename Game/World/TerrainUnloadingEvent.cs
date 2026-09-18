using Engine.Math;

namespace Game.World;

/// <summary>Published by NeighborhoodStreamer for an unloading neighborhood's terrain while it can still be read: one area, a row at a time, after every entity there is destroyed and before its stores are dropped.</summary>
/// <remarks>
/// Consumers: the aura grids on both sides, which unsplat the area's terrain auras (they reach into
/// loaded neighbors too), and MapWindow's cached terrain image when the area is on screen. Entity aura
/// sources are already gone by then, removed as each entity was destroyed.
/// </remarks>
public readonly record struct TerrainUnloadingEvent(MapBounds Area);
