using Engine.Math;

namespace Game.World;

/// <summary>Every neighborhood record the session has assigned, by neighborhood coordinate.</summary>
/// <remarks>
/// A record's seed is rolled from the session's MathUtility the first time its coordinate is needed,
/// so which seed a coordinate gets depends on the order coordinates are first needed. Nothing generated
/// from a record reads the session's random state again.
/// </remarks>
public sealed class NeighborhoodRecords(MathUtility mathUtility)
{
    private readonly Dictionary<(int CellX, int CellY), NeighborhoodRecord> _records = [];

    public int Count => _records.Count;

    /// <summary>The record for neighborhood (cellX, cellY), assigning it a seed if it has none yet.</summary>
    public NeighborhoodRecord GetOrCreate(int cellX, int cellY)
    {
        if (!_records.TryGetValue((cellX, cellY), out var record))
        {
            record = new NeighborhoodRecord(cellX, cellY, mathUtility.Next(int.MinValue, int.MaxValue));
            _records.Add((cellX, cellY), record);
        }

        return record;
    }

    public bool TryGet(int cellX, int cellY, out NeighborhoodRecord record) => _records.TryGetValue((cellX, cellY), out record!);
}
