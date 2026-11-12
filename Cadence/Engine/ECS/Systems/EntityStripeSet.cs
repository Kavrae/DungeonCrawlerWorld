using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary> Maintains a stable entityId -> stripe assignment for entity striping: ids in blocks of <see cref="BlockSize"/>, the blocks dealt round-robin across the stripes.</summary>
///<remarks>
/// A whole block shares a stripe, so a bucket visits runs of neighbouring ids and each cache line of an entity-indexed
/// array it reads serves a block rather than one entity. Round-robin keeps the buckets as even as plain id-modulo
/// striping for contiguous ids. Bucket membership is maintained incrementally via the source pool's
/// EntityAdded/EntityRemoved events (subscribed once in the owning system's constructor).
///</remarks>
///<cleanupVersion>2</cleanupVersion>
public sealed class EntityStripeSet
{
    private const int BlockShift = 4;

    /// <summary>How many consecutive entity ids share a stripe.</summary>
    public const int BlockSize = 1 << BlockShift;

    private readonly List<int>[] _buckets;
    private readonly EntityPages<int> _indexInBucketByEntityId;
    private readonly ushort _stripeCount;

    /// <summary>How many stripes entities are divided across: see <see cref="StripeOf"/>.</summary>
    /// <remarks>A ushort, since TieredEntityStripeSet multiplies a byte stripe count by a byte tier divisor.</remarks>
    public ushort StripeCount => _stripeCount;

    public EntityStripeSet(ushort stripeCount, ReadOnlySpan<int> existingEntityIds)
        : this(stripeCount, new EntityPages<int>(entityCapacity: 1, empty: -1))
    {
        foreach (var entityId in existingEntityIds)
        {
            OnEntityAdded(entityId);
        }
    }

    /// <summary>A set whose index-in-bucket table is shared with other sets an entity is never in at the same time (TieredEntityStripeSet's tiers).</summary>
    internal EntityStripeSet(ushort stripeCount, EntityPages<int> sharedIndexInBucketByEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(stripeCount);

        _stripeCount = stripeCount;
        _indexInBucketByEntityId = sharedIndexInBucketByEntityId;
        _buckets = new List<int>[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _buckets[i] = [];
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

    /// <summary>The stripe entityId belongs to in a set of stripeCount stripes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StripeOf(int entityId, int stripeCount) => (entityId >> BlockShift) % stripeCount;

    /// <summary>Gets the entities assigned to the given stripe.</summary>
    /// <param name="stripeIndex">The index of the stripe to retrieve entities for.</param>
    /// <returns>A read-only span containing the entities in the specified stripe.</returns>
    public ReadOnlySpan<int> GetBucket(ushort stripeIndex) => CollectionsMarshal.AsSpan(_buckets[stripeIndex]);

    /// <summary>Handles the addition of a new entity to the set.</summary>
    /// <remarks>An entity already in the set stays where it is.</remarks>
    /// <param name="entityId">The ID of the entity being added.</param>
    public void OnEntityAdded(int entityId)
    {
        if (_indexInBucketByEntityId.Get(entityId) >= 0)
        {
            return;
        }

        var bucket = _buckets[StripeOf(entityId, _stripeCount)];
        _indexInBucketByEntityId.Claim(entityId) = bucket.Count;
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

        var bucket = _buckets[StripeOf(entityId, _stripeCount)];
        var lastIndex = bucket.Count - 1;
        var lastEntityId = bucket[lastIndex];

        bucket[indexInBucket] = lastEntityId;
        bucket.RemoveAt(lastIndex);

        if (lastEntityId != entityId)
        {
            _indexInBucketByEntityId.GetExisting(lastEntityId) = indexInBucket;
        }

        _indexInBucketByEntityId.Release(entityId);
    }
}
