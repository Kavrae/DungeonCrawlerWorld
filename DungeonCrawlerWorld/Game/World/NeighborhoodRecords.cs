namespace Game.World;

/// <summary>Every neighborhood record the session has assigned, by neighborhood coordinate.</summary>
/// <remarks>
/// A record is created the first time its coordinate is needed, but its seed is derived from the session seed, the floor
/// and the coordinate (NeighborhoodSeeds), so it is the same whenever that is. Nothing generated from a record reads any
/// random sequence of the session's.
/// </remarks>
/// <param name="worldSeed">The session seed.</param>
/// <param name="floorNumber">The floor these neighborhoods are on.</param>
public sealed class NeighborhoodRecords(int worldSeed, int floorNumber)
{
    private readonly Dictionary<(int CellX, int CellY), NeighborhoodRecord> _records = [];

    public int Count => _records.Count;

    /// <summary>The record for neighborhood (cellX, cellY), creating it if it has none yet.</summary>
    public NeighborhoodRecord GetOrCreate(int cellX, int cellY)
    {
        if (!_records.TryGetValue((cellX, cellY), out var record))
        {
            record = new NeighborhoodRecord(cellX, cellY, NeighborhoodSeeds.ForLayout(worldSeed, floorNumber, cellX, cellY));
            _records.Add((cellX, cellY), record);
        }

        return record;
    }

    public bool TryGet(int cellX, int cellY, out NeighborhoodRecord record) => _records.TryGetValue((cellX, cellY), out record!);
}
