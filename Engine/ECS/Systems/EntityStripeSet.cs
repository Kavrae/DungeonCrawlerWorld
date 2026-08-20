using System.Runtime.InteropServices;
using Engine.ECS.Components;

namespace Engine.ECS.Systems;

/// <summary> Maintains a stable entityId -> stripe assignment (entityId % StripeCount) for entity striping</summary>
///<remarks> Bucket membership is maintained incrementally via the source pool's EntityAdded/EntityRemoved events (subscribed once in the owning system's constructor). </remarks>
///<cleanupVersion>1</cleanupVersion>
public sealed class EntityStripeSet
{
    private readonly List<int>[] _buckets;
    private readonly Dictionary<int, (byte Stripe, int IndexInBucket)> _locationsByEntityId = [];
    private readonly byte _stripeCount;

    public byte StripeCount => _stripeCount;

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

    /// <summary>Builds an EntityStripeSet already wired to drivingPool's EntityAdded/EntityRemoved membership events -- the construct-then-subscribe dance every non-tiered EntityStripeSet consumer's constructor would otherwise repeat by hand.</summary>
    /// <param name="stripeCount">The number of stripes to divide entities across.</param>
    /// <param name="drivingPool">The pool whose membership the stripe set should track.</param>
    public static EntityStripeSet CreateAndWire(byte stripeCount, IEntityMembershipPool drivingPool)
    {
        var stripeSet = new EntityStripeSet(stripeCount, drivingPool.EntityIds);
        drivingPool.EntityAdded += stripeSet.OnEntityAdded;
        drivingPool.EntityRemoved += stripeSet.OnEntityRemoved;
        return stripeSet;
    }

    /// <summary>Gets the entities assigned to the given stripe.</summary>
    /// <param name="stripeIndex">The index of the stripe to retrieve entities for.</param>
    /// <returns>A read-only span containing the entities in the specified stripe.</returns>
    public ReadOnlySpan<int> GetBucket(byte stripeIndex) => CollectionsMarshal.AsSpan(_buckets[stripeIndex]);

    /// <summary>Handles the addition of a new entity to the set.</summary>
    /// <param name="entityId">The ID of the entity being added.</param>
    public void OnEntityAdded(int entityId)
    {
        var stripe = (byte)(entityId % _stripeCount);
        var bucket = _buckets[stripe];

        _locationsByEntityId[entityId] = (stripe, bucket.Count);
        bucket.Add(entityId);
    }

    /// <summary>Handles the removal of an entity from the set.</summary>
    /// <param name="entityId">The ID of the entity being removed.</param>
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