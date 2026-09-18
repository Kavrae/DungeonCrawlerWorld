namespace Game.Modules.ProcessingTier;

/// <summary>Neighborhoods waiting to have their entities' tiers recomputed after the player crossed into another one, in two bands so thawing runs ahead of freezing.</summary>
/// <remarks>
/// <para>
/// A neighborhood crossing changes the tier of every entity in several whole neighborhoods at once
/// -- on the 3x3 map that is over 300,000 entities. Doing that in the frame the player stepped is a
/// guaranteed hitch, so the affected neighborhoods are queued and their entities drained under a
/// per-frame budget instead.
/// </para>
/// <para>
/// What is queued is the neighborhood, not its entities: copying every id up front cost 3.2ms of
/// the crossing frame by itself. A queued neighborhood's membership is copied one neighborhood at a
/// time, as the drain reaches it, and walked with a cursor across frames.
/// </para>
/// <para>
/// No target tier is stored either: the drain recomputes each entity's tier from the reference
/// position as it stands at that moment. A player who crosses back before the queue drains
/// therefore cancels the pending work by making it a no-op, rather than needing the opposite entry
/// found and removed. For the same reason a neighborhood queued twice is harmless.
/// </para>
/// <para>
/// The thaw band holds neighborhoods becoming simulated, the other band everything else. Thawing
/// first is what the player actually walks into; freezing a neighborhood behind them a few frames
/// late only costs a few extra visits at Neighborhood speed.
/// </para>
/// </remarks>
public sealed class ProcessingTierTransitionQueue
{
    private readonly Queue<(int CellX, int CellY, int Z)> _thawing = [];
    private readonly Queue<(int CellX, int CellY, int Z)> _other = [];

    /// <summary>The neighborhood being drained right now, copied once when the drain reached it -- see this class's own remarks.</summary>
    private readonly List<int> _currentEntityIds = [];

    private int _cursor;

    /// <summary>Whether any entity is still waiting to be recomputed.</summary>
    public bool HasPending => _cursor < _currentEntityIds.Count || _thawing.Count > 0 || _other.Count > 0;

    /// <summary>How many neighborhoods are queued, not counting the one being drained.</summary>
    public int PendingNeighborhoodCount => _thawing.Count + _other.Count;

    /// <summary>Queues one neighborhood layer, in the thaw band when it is becoming simulated.</summary>
    public void Enqueue(int cellX, int cellY, int z, bool isThawing) => (isThawing ? _thawing : _other).Enqueue((cellX, cellY, z));

    /// <summary>Takes the next waiting entity id, copying the next queued neighborhood's membership when the current one runs out. Thaw band first.</summary>
    public bool TryDequeue(NeighborhoodMembershipIndex membership, out int entityId)
    {
        ArgumentNullException.ThrowIfNull(membership);

        while (_cursor >= _currentEntityIds.Count)
        {
            if (!_thawing.TryDequeue(out var cell) && !_other.TryDequeue(out cell))
            {
                entityId = -1;
                return false;
            }

            _currentEntityIds.Clear();
            _cursor = 0;
            membership.CopyCell(cell.CellX, cell.CellY, cell.Z, _currentEntityIds);
        }

        entityId = _currentEntityIds[_cursor++];
        return true;
    }
}
