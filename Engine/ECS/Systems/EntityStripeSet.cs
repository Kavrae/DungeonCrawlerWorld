namespace Engine.ECS.Systems;

/// <summary>
/// Maintains a stable entityId -> stripe assignment (entityId % StripeCount) for entity
/// striping, instead of a system re-deriving "which entities belong to this frame's stripe"
/// from a pool's live dense indices every call. Dense indices are not stable per-entity
/// identifiers: PackedComponentPool.Remove fills a removed slot by swapping in whatever
/// entity currently sits last, so an index-range stripe can silently pick up a different
/// entity than it had last cycle -- which can cause the same entity to be processed twice in
/// one cycle (if churn moves it into a not-yet-visited index) or skipped for a cycle (if
/// churn moves it into an already-visited index). Bucketing by entityId instead is immune to
/// this: an entity's bucket never changes for as long as it lives, regardless of what
/// happens elsewhere in the pool, so processing is exactly-once per full cycle for every
/// entity present throughout it.
///
/// Bucket membership is maintained incrementally via the source pool's EntityAdded/
/// EntityRemoved events (subscribed once in the owning system's constructor) rather than by
/// rescanning the pool, so there is no periodic O(Count) rebuild to reintroduce the very
/// per-frame cost spike striping exists to avoid -- every operation here (seed aside) is
/// O(1): appending on add, swap-with-last-in-bucket on remove.
/// </summary>
public sealed class EntityStripeSet
{
    private readonly List<int>[] _buckets;
    private readonly Dictionary<int, (byte Stripe, int IndexInBucket)> _locationsByEntityId = [];
    private readonly byte _stripeCount;

    public EntityStripeSet(byte stripeCount, ReadOnlySpan<int> existingEntityIds)
    {
        ArgumentOutOfRangeException.ThrowIfZero(stripeCount);

        _stripeCount = stripeCount;
        _buckets = new List<int>[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _buckets[i] = [];
        }

        foreach (var entityId in existingEntityIds)
        {
            OnEntityAdded(entityId);
        }
    }

    /// <summary>The entities assigned to the given stripe as of right now. Do not mutate the source pool while enumerating this.</summary>
    public IReadOnlyList<int> GetBucket(byte stripeIndex) => _buckets[stripeIndex];

    public void OnEntityAdded(int entityId)
    {
        var stripe = (byte)(entityId % _stripeCount);
        var bucket = _buckets[stripe];

        _locationsByEntityId[entityId] = (stripe, bucket.Count);
        bucket.Add(entityId);
    }

    public void OnEntityRemoved(int entityId)
    {
        if (!_locationsByEntityId.Remove(entityId, out var location))
        {
            return;
        }

        var bucket = _buckets[location.Stripe];
        var lastIndex = bucket.Count - 1;
        var lastEntityId = bucket[lastIndex];

        bucket[location.IndexInBucket] = lastEntityId;
        bucket.RemoveAt(lastIndex);

        if (lastEntityId != entityId)
        {
            _locationsByEntityId[lastEntityId] = (location.Stripe, location.IndexInBucket);
        }
    }
}