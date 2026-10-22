namespace Game.Areas;

/// <summary>What one legend symbol means in a cell: its floor, the wall on it, and what spawns there. Any part left out keeps what generation put there.</summary>
/// <param name="Terrain">The terrain key of the floor beneath the cell's layer; not allowed on Flying, which has no floor.</param>
/// <param name="Structure">The terrain key of a wall standing on the cell's layer.</param>
/// <param name="ClearsStructure">Removes a wall generation put there; never with Structure.</param>
/// <param name="Spawns">What spawns with its footprint's top-left at the cell, on the cell's layer.</param>
public sealed record AreaCell(
    char Symbol,
    string? Terrain = null,
    string? Structure = null,
    bool ClearsStructure = false,
    IReadOnlyList<AreaSpawn>? Spawns = null)
{
    public IReadOnlyList<AreaSpawn> SpawnList => Spawns ?? [];
}
