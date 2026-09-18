namespace Game.Terrain;

/// <summary>One cell's terrain: which definition, and which of its sprite variants.</summary>
/// <param name="TypeId">TerrainRegistry id; <see cref="TerrainRegistry.None"/> for no terrain.</param>
/// <param name="Variant">Index into the definition's sprite cells, rolled once when the cell is set.</param>
public readonly record struct TerrainCell(ushort TypeId, byte Variant)
{
    public bool IsEmpty => TypeId == TerrainRegistry.None;
}
