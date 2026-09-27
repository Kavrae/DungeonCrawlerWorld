namespace Game.Modules.ProcessingTier;

/// <summary>Neighborhoods waiting to have their entities' tiers recomputed after the player crossed into another one, in three bands: thawing, then freezing, then changes between unsimulated tiers.</summary>
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
/// Bands (ProcessingTierTransitionBand) drain in order. Thawing first is what the player actually
/// walks into; freezing a neighborhood behind them a few frames late only costs a few extra visits at
/// Neighborhood speed. A change between two unsimulated tiers (Borough and Beyond) goes last: nothing
/// visits either tier, so it only has to be exact eventually, and it is most of a shift's work.
/// </para>
/// <para>
/// The thaw band can be held (TryDequeue's thawingHeld) while the other bands keep draining: thawing
/// builds every creature skeleton it reaches, and holding it until the neighborhoods leaving the
/// window are unloaded lets those builds reuse the storage the unloaded creatures free.
/// </para>
/// </remarks>
public sealed class ProcessingTierTransitionQueue
{
    private readonly Queue<(int CellX, int CellY, int Z)> _thawing = [];
    private readonly Queue<(int CellX, int CellY, int Z)> _freezing = [];
    private readonly Queue<(int CellX, int CellY, int Z)> _unsimulated = [];

    /// <summary>The neighborhood being drained right now, copied once when the drain reached it -- see this class's own remarks.</summary>
    private readonly List<int> _currentEntityIds = [];

    private int _cursor;

    /// <summary>The band of the neighborhood being drained right now.</summary>
    private ProcessingTierTransitionBand _currentBand;

    /// <summary>Whether any entity is still waiting to be recomputed.</summary>
    public bool HasPending => _cursor < _currentEntityIds.Count || PendingNeighborhoodCount > 0;

    /// <summary>Whether any change that crosses the simulated boundary (the thaw and freeze bands) is still waiting. Once false, everything the simulation can see has settled; only moves between unsimulated tiers remain.</summary>
    public bool HasPendingSimulatedChanges =>
        (_cursor < _currentEntityIds.Count && _currentBand != ProcessingTierTransitionBand.Unsimulated) || _thawing.Count > 0 || _freezing.Count > 0;

    /// <summary>How many neighborhoods are queued, not counting the one being drained.</summary>
    public int PendingNeighborhoodCount => _thawing.Count + _freezing.Count + _unsimulated.Count;

    /// <summary>Queues one neighborhood layer in band.</summary>
    public void Enqueue(int cellX, int cellY, int z, ProcessingTierTransitionBand band) => (band switch
    {
        ProcessingTierTransitionBand.Thawing => _thawing,
        ProcessingTierTransitionBand.Freezing => _freezing,
        _ => _unsimulated,
    }).Enqueue((cellX, cellY, z));

    /// <summary>Takes the next waiting entity id, copying the next queued neighborhood's membership when the current one runs out. Bands in order, skipping the thaw band while thawingHeld.</summary>
    /// <param name="thawingHeld">Starts no thaw-band neighborhood; one already being drained finishes.</param>
    public bool TryDequeue(NeighborhoodMembershipIndex membership, out int entityId, bool thawingHeld = false)
    {
        ArgumentNullException.ThrowIfNull(membership);

        while (_cursor >= _currentEntityIds.Count)
        {
            var cell = default((int CellX, int CellY, int Z));
            if (!thawingHeld && _thawing.TryDequeue(out cell))
            {
                _currentBand = ProcessingTierTransitionBand.Thawing;
            }
            else if (_freezing.TryDequeue(out cell))
            {
                _currentBand = ProcessingTierTransitionBand.Freezing;
            }
            else if (_unsimulated.TryDequeue(out cell))
            {
                _currentBand = ProcessingTierTransitionBand.Unsimulated;
            }
            else
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
