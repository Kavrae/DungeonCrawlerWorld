using Engine.Math;

namespace Game.World;

/// <summary>One occupied cell of an OccupiedCellScan.</summary>
/// <remarks>Occupants is valid until that cell's occupants change, as GetOccupantEntityIdSpanAt's span is.</remarks>
public readonly ref struct OccupiedCell
{
    internal OccupiedCell(Vector3Int position, ReadOnlySpan<int> occupants)
    {
        Position = position;
        Occupants = occupants;
    }

    public Vector3Int Position { get; }

    public ReadOnlySpan<int> Occupants { get; }
}
