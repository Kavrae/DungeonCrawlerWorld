using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>Published by World.SetTerrain whenever a cell's terrain changes -- the signal anything derived from terrain needs to update.</summary>
/// <remarks>
/// Consumers: MapWindow's cached terrain image (invalidate), and the AuraField, which takes the
/// previous terrain's aura out and puts the new one in -- which is why the event carries both type ids.
///
/// Immediate, not IBufferedEvent: terrain changes are rare (an explosion, a spell), not per-move,
/// and no consumer writes a pool another system could be mid-scan over. Population doesn't publish
/// at all -- see World.PopulateTerrain.
/// </remarks>
/// <param name="X">The x-coordinate of the cell whose terrain changed.</param>
/// <param name="Y">The y-coordinate of the cell whose terrain changed.</param>
/// <param name="TerrainLayer">The terrain layer that changed at that cell.</param>
/// <param name="PreviousTypeId">The terrain the cell held before, or TerrainRegistry.None.</param>
/// <param name="TypeId">The terrain the cell holds now, or TerrainRegistry.None.</param>
public readonly record struct TerrainChangedEvent(int X, int Y, TerrainLayer TerrainLayer, ushort PreviousTypeId, ushort TypeId);
