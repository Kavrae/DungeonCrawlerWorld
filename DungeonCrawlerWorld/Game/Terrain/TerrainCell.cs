namespace Game.Terrain;

/// <summary>One cell's terrain: which definition. How it looks is picked from the cell's seed (World.CellSeeds), not stored.</summary>
/// <param name="TypeId">TerrainRegistry id; <see cref="TerrainRegistry.None"/> for no terrain.</param>
public readonly record struct TerrainCell(ushort TypeId)
{
    public bool IsEmpty => TypeId == TerrainRegistry.None;
}
