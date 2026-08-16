namespace Engine.ECS.Systems;

/// <summary> Generalizes EntityStripeSet into N independently-striped tiers, each visited at its own cadence. </summary>
/// <remarks>
/// Entities are divided into buckets by tier, and each tier's own bucket is striped independently of the others.
/// Whenever an entity is added, removed, or has its tier changed, the appropriate tier's own EntityStripeSet is updated accordingly.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class TieredEntityStripeSet
{
    private readonly EntityStripeSet[] _tierBuckets;
    private readonly Func<int, byte> _currentTierIndexLookup;
    private readonly Dictionary<int, byte> _memberTierIndexByEntityId = [];

    /// <summary>Initializes a new instance of the <see cref="TieredEntityStripeSet"/> class.</summary>
    /// <param name="baseStripeCount">The base number of stripes for each tier.</param>
    /// <param name="tierDivisors">Specifies how much less frequently each tier is visited compared to the base.</param>
    /// <param name="existingEntityIds">The IDs to be added to the set on initialization.</param>
    /// <param name="currentTierIndexLookup">The function to determine the current tier index for an entity.</param>
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

    /// <summary>Places a newly-added entity into their assigned tier and bucket.</summary>
    public void OnMemberAdded(int entityId)
    {
        var tierIndex = _currentTierIndexLookup(entityId);
        _memberTierIndexByEntityId[entityId] = tierIndex;
        _tierBuckets[tierIndex].OnEntityAdded(entityId);
    }

    /// <summary>Removes an entity from their assigned tier and bucket.</summary>
    /// <param name="entityId">The ID of the entity to remove.</param>
    public void OnMemberRemoved(int entityId)
    {
        if (_memberTierIndexByEntityId.Remove(entityId, out var tierIndex))
        {
            _tierBuckets[tierIndex].OnEntityRemoved(entityId);
        }
    }

    /// <summary>Updates the tier assignment for an entity that has moved to a different tier.</summary>
    /// <param name="entityId">The ID of the entity to update.</param>
    /// <param name="newTierIndex">The new tier index for the entity.</param>
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

    /// <summary>The entities due for full processing this frame, chained across every tier's own current bucket.</summary>
    /// <remarks></remarks>Do not mutate any source pool while enumerating this.</remarks>
    public DueEntitiesEnumerable GetDueEntities(long frameCount) => new(_tierBuckets, frameCount);

    /// <summary>How many tiers this set was constructed with.</summary>
    public int TierCount => _tierBuckets.Length;

    /// <summary>Gets the bucket to process for a specific tier at the given frame count.</summary>
    /// <param name="tierIndex">The index of the tier.</param>
    /// <param name="frameCount">The frame count.</param>
    /// <returns>The bucket for the specified tier and frame count.</returns>
    public ReadOnlySpan<int> GetTierBucket(int tierIndex, long frameCount)
    {
        var bucket = _tierBuckets[tierIndex];
        return bucket.GetBucket((byte)(frameCount % bucket.StripeCount));
    }

    /// <summary>Gets the number of frames between visits for a specific tier.</summary>
    /// <param name="tierIndex">The index of the tier.</param>
    /// <returns>The number of frames per visit for the specified tier.</returns>
    public byte GetTierFramesPerVisit(int tierIndex) => _tierBuckets[tierIndex].StripeCount;

    /// <summary>Enumerates the entities due for processing this frame, chained across every tier's own current bucket.</summary>
    /// <remarks>
    /// Allocation-free chained enumerator over every tier's current bucket.
    /// </remarks>
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

        /// <summary>Gets the enumerator for the due entities.</summary>
        /// <returns>The enumerator for the due entities.</returns>
        public readonly DueEntitiesEnumerable GetEnumerator() => this;

        /// <summary>Gets the current entity in the enumeration.</summary>
        public readonly int Current => _currentSpan[_indexInSpan];

        /// <summary>Moves to the next entity in the enumeration.</summary>
        /// <remarks>Walks each tier's current bucket in turn.</remarks>
        /// <returns></returns>
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
