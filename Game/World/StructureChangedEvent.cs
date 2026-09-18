using Engine.Math;

namespace Game.World;

/// <summary>Published by World.SetStructure whenever a cell's structure changes -- the signal anything derived from structures needs to update.</summary>
/// <remarks>
/// Consumer: MapWindow's cached terrain image, which draws structures. Immediate for the same
/// reasons as TerrainChangedEvent, and population doesn't publish -- see World.PopulateStructure.
/// Occupants already standing in the cell are not moved out when a blocking structure appears.
/// </remarks>
/// <param name="Position">The cell whose structure changed; Z is its MapLayer.</param>
/// <param name="PreviousTypeId">The structure the cell held before, or TerrainRegistry.None.</param>
/// <param name="TypeId">The structure the cell holds now, or TerrainRegistry.None.</param>
public readonly record struct StructureChangedEvent(Vector3Int Position, ushort PreviousTypeId, ushort TypeId);
