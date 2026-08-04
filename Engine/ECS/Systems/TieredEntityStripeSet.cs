namespace Engine.ECS.Systems;

/// <summary>
/// Generalizes EntityStripeSet into N independently-striped tiers, each visited at its own
/// cadence (tierDivisors[i] multiplies baseStripeCount for tier i), instead of striping the
/// whole population uniformly and having each consumer skip entities that aren't "due" this
/// visit. A skipped visit under uniform striping still pays for the skip check itself (a pool
/// read + a branch) on every single stripe turn; a tiered entity that isn't due this cycle
/// simply isn't in any bucket a caller visits this frame, so nothing is paid at all.
///
/// Deliberately generic over what a "tier" means -- this class never references any concrete
/// tier concept (e.g. distance-from-player). The caller supplies tier count and per-tier
/// divisors as plain data, and a currentTierIndexLookup delegate ("what tier index is this
/// entity in right now") to resolve a newly-added member's starting bucket -- the same pattern
/// CountdownTicker.Tick's shouldProcess parameter already uses to keep Engine ignorant of
/// Game's tier concept.
///
/// Migration (an entity moving from one tier to another) is the cost this trades against the
/// uniform-striping-plus-skip-check design: OnEntityTierChanged does two EntityStripeSet
/// operations (remove from the old bucket, add to the new one), both O(1) swap-based, not a
/// data copy -- cheap as long as tier changes are relatively rare events (e.g. spatial boundary
/// crossings), not something that happens every visit.
/// </summary>
public sealed class TieredEntityStripeSet
{
    private readonly EntityStripeSet[] _tierBuckets;
    private readonly Func<int, byte> _currentTierIndexLookup;
    private readonly Dictionary<int, byte> _memberTierIndexByEntityId = [];

    public TieredEntityStripeSet(byte baseStripeCount, ReadOnlySpan<byte> tierDivisors, ReadOnlySpan<int> existingEntityIds, Func<int, byte> currentTierIndexLookup)
    {
        ArgumentNullException.ThrowIfNull(currentTierIndexLookup);
        ArgumentOutOfRangeException.ThrowIfZero(tierDivisors.Length);

        _currentTierIndexLookup = currentTierIndexLookup;
        _tierBuckets = new EntityStripeSet[tierDivisors.Length];
        for (var i = 0; i < tierDivisors.Length; i++)
        {
            _tierBuckets[i] = new EntityStripeSet((byte)(baseStripeCount * tierDivisors[i]), []);
        }

        foreach (var entityId in existingEntityIds)
        {
            OnMemberAdded(entityId);
        }
    }

    /// <summary>Places a newly-added member into whichever tier currentTierIndexLookup reports right now -- not necessarily "just created" (see e.g. a system whose population is driven by an exposure component that can be granted to an entity that's had a real tier for a long time already).</summary>
    public void OnMemberAdded(int entityId)
    {
        var tierIndex = _currentTierIndexLookup(entityId);
        _memberTierIndexByEntityId[entityId] = tierIndex;
        _tierBuckets[tierIndex].OnEntityAdded(entityId);
    }

    public void OnMemberRemoved(int entityId)
    {
        if (_memberTierIndexByEntityId.Remove(entityId, out var tierIndex))
        {
            _tierBuckets[tierIndex].OnEntityRemoved(entityId);
        }
    }

    /// <summary>
    /// A tier-change source (e.g. a shared distance-from-player recompute) typically fans this
    /// out to every entity it tracks, regardless of whether THIS particular
    /// TieredEntityStripeSet's own population includes that entity -- the
    /// _memberTierIndexByEntityId lookup makes that a no-op for anyone not currently a member
    /// here, so a system's own population definition (driven entirely by its own OnMemberAdded/
    /// OnMemberRemoved source) never silently grows to match some other, unrelated population.
    /// </summary>
    public void OnEntityTierChanged(int entityId, byte newTierIndex)
    {
        if (!_memberTierIndexByEntityId.TryGetValue(entityId, out var oldTierIndex) || oldTierIndex == newTierIndex)
        {
            return;
        }

        _tierBuckets[oldTierIndex].OnEntityRemoved(entityId);
        _tierBuckets[newTierIndex].OnEntityAdded(entityId);
        _memberTierIndexByEntityId[entityId] = newTierIndex;
    }

    /// <summary>The entities due for full processing this frame, chained across every tier's own current bucket. Do not mutate any source pool while enumerating this -- same rule as EntityStripeSet.GetBucket.</summary>
    public DueEntitiesEnumerable GetDueEntities(long frameCount) => new(_tierBuckets, frameCount);

    /// <summary>How many tiers this set was constructed with -- for a caller that needs to visit each tier's own bucket individually (e.g. CountdownTicker.Tick, which needs a true ReadOnlySpan and each tier's own framesPerVisit, not the chained GetDueEntities sequence).</summary>
    public int TierCount => _tierBuckets.Length;

    /// <summary>Tier tierIndex's own current bucket, as a true ReadOnlySpan (unlike GetDueEntities, which can only chain across tiers, not hand out one contiguous span per tier).</summary>
    public ReadOnlySpan<int> GetTierBucket(int tierIndex, long frameCount)
    {
        var bucket = _tierBuckets[tierIndex];
        return bucket.GetBucket((byte)(frameCount % bucket.StripeCount));
    }

    /// <summary>Tier tierIndex's own internal stripe count -- the real-frame interval between consecutive visits to any given entity in that tier, i.e. the framesPerVisit a caller like CountdownTicker.Tick needs to pass for that tier's bucket.</summary>
    public byte GetTierFramesPerVisit(int tierIndex) => _tierBuckets[tierIndex].StripeCount;

    /// <summary>
    /// Allocation-free chained enumerator over every tier's current bucket -- a true
    /// ReadOnlySpan can't span the tiers' separate backing arrays, so this walks each tier's
    /// own EntityStripeSet.GetBucket span in turn instead of copying them into one contiguous
    /// buffer. Doubles as its own enumerator (GetEnumerator returns a copy of itself) the same
    /// way ReadOnlySpan&lt;T&gt;.Enumerator does, so `foreach` over GetDueEntities(...) allocates
    /// nothing.
    /// </summary>
    public ref struct DueEntitiesEnumerable
    {
        private readonly EntityStripeSet[] _tierBuckets;
        private readonly long _frameCount;
        private int _tierIndex;
        private ReadOnlySpan<int> _currentSpan;
        private int _indexInSpan;

        internal DueEntitiesEnumerable(EntityStripeSet[] tierBuckets, long frameCount)
        {
            _tierBuckets = tierBuckets;
            _frameCount = frameCount;
            _tierIndex = -1;
            _currentSpan = default;
            _indexInSpan = -1;
        }

        public readonly DueEntitiesEnumerable GetEnumerator() => this;

        public readonly int Current => _currentSpan[_indexInSpan];

        public bool MoveNext()
        {
            while (true)
            {
                if (_indexInSpan + 1 < _currentSpan.Length)
                {
                    _indexInSpan++;
                    return true;
                }

                _tierIndex++;
                if (_tierIndex >= _tierBuckets.Length)
                {
                    return false;
                }

                var bucket = _tierBuckets[_tierIndex];
                _currentSpan = bucket.GetBucket((byte)(_frameCount % bucket.StripeCount));
                _indexInSpan = -1;
            }
        }
    }
}
