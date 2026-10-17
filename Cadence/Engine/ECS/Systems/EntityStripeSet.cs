using System.Runtime.InteropServices;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary> Maintains a stable entityId -> stripe assignment (entityId % StripeCount) for entity striping</summary>
///<remarks> Bucket membership is maintained incrementally via the source pool's EntityAdded/EntityRemoved events (subscribed once in the owning system's constructor). </remarks>
///<cleanupVersion>1</cleanupVersion>
public sealed class EntityStripeSet
{
    private readonly List<int>[] _buckets;
    private readonly EntityPages<int> _indexInBucketByEntityId = new(entityCapacity: 1, empty: -1);
    private readonly ushort _stripeCount;

    /// <summary>How many stripes entities are divided across: an entity's stripe is its id modulo this.</summary>
    /// <remarks>A ushort, since TieredEntityStripeSet multiplies a byte stripe count by a byte tier divisor.</remarks>
    public ushort StripeCount => _stripeCount;

    public EntityStripeSet(ushort stripeCount, ReadOnlySpan<int> existingEntityIds)
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

    /// <summary>Builds an EntityStripeSet already wired to drivingPool's EntityAdded/EntityRemoved membership events.</summary>
    /// <param name="stripeCount">The number of stripes to divide entities across.</param>
    /// <param name="drivingPool">The pool whose membership the stripe set should track.</param>
    public static EntityStripeSet CreateAndWire(ushort stripeCount, IEntityMembershipPool drivingPool)
    {
        var stripeSet = new EntityStripeSet(stripeCount, drivingPool.EntityIds);
        drivingPool.EntityAdded += stripeSet.OnEntityAdded;
        drivingPool.EntityRemoved += stripeSet.OnEntityRemoved;
        return stripeSet;
    }

    /// <summary>Gets the entities assigned to the given stripe.</summary>
    /// <param name="stripeIndex">The index of the stripe to retrieve entities for.</param>
    /// <returns>A read-only span containing the entities in the specified stripe.</returns>
    public ReadOnlySpan<int> GetBucket(ushort stripeIndex) => CollectionsMarshal.AsSpan(_buckets[stripeIndex]);

    /// <summary>Handles the addition of a new entity to the set.</summary>
    /// <remarks>An entity already in the set stays where it is.</remarks>
    /// <param name="entityId">The ID of the entity being added.</param>
    public void OnEntityAdded(int entityId)
    {
        ref var indexInBucket = ref _indexInBucketByEntityId.GetWritable(entityId);
        if (indexInBucket >= 0)
        {
            return;
        }

        var bucket = _buckets[entityId % _stripeCount];
        indexInBucket = bucket.Count;
        bucket.Add(entityId);
    }

    /// <summary>Handles the removal of an entity from the set.</summary>
    /// <param name="entityId">The ID of the entity being removed.</param>
    public void OnEntityRemoved(int entityId)
    {
        var indexInBucket = _indexInBucketByEntityId.Get(entityId);
        if (indexInBucket < 0)
        {
            return;
        }

        var bucket = _buckets[entityId % _stripeCount];
        var lastIndex = bucket.Count - 1;
        var lastEntityId = bucket[lastIndex];

        bucket[indexInBucket] = lastEntityId;
        bucket.RemoveAt(lastIndex);

        if (lastEntityId != entityId)
        {
            _indexInBucketByEntityId.GetWritable(lastEntityId) = indexInBucket;
        }

        _indexInBucketByEntityId.GetWritable(entityId) = -1;
    }
}
