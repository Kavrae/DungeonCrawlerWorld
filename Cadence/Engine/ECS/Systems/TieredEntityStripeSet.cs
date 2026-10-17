using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary> Generalizes EntityStripeSet into N independently-striped tiers, each visited at its own cadence. </summary>
/// <remarks>
/// Entities are divided into buckets by tier, and each tier's own bucket is striped independently of the others.
/// Whenever an entity is added, removed, or has its tier changed, the appropriate tier's own EntityStripeSet is updated accordingly.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class TieredEntityStripeSet
{
    private const byte NotAMember = byte.MaxValue;

    private readonly EntityStripeSet[] _tierBuckets;
    private readonly Func<int, byte> _currentTierIndexLookup;
    private readonly EntityPages<byte> _memberTierIndexByEntityId = new(entityCapacity: 1, empty: NotAMember);

    /// <summary>Initializes a new instance of the <see cref="TieredEntityStripeSet"/> class.</summary>
    /// <param name="baseStripeCount">The base number of stripes for each tier.</param>
    /// <param name="tierDivisors">Specifies how much less frequently each tier is visited compared to the base.</param>
    /// <param name="existingEntityIds">The IDs to be added to the set on initialization.</param>
    /// <param name="currentTierIndexLookup">The function to determine the current tier index for an entity.</param>
    public TieredEntityStripeSet(byte baseStripeCount, ReadOnlySpan<byte> tierDivisors, ReadOnlySpan<int> existingEntityIds, Func<int, byte> currentTierIndexLookup)
    {
        ArgumentNullException.ThrowIfNull(currentTierIndexLookup);
        ArgumentOutOfRangeException.ThrowIfZero(tierDivisors.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tierDivisors.Length, (int)NotAMember);

        _currentTierIndexLookup = currentTierIndexLookup;
        _tierBuckets = new EntityStripeSet[tierDivisors.Length];
        for (var i = 0; i < tierDivisors.Length; i++)
        {
            _tierBuckets[i] = new EntityStripeSet((ushort)(baseStripeCount * tierDivisors[i]), []);
        }

        foreach (var entityId in existingEntityIds)
        {
            OnMemberAdded(entityId);
        }
    }

    /// <summary>Places a newly-added entity into their assigned tier and bucket.</summary>
    /// <remarks>An entity already in the set stays where it is.</remarks>
    public void OnMemberAdded(int entityId)
    {
        ref var memberTierIndex = ref _memberTierIndexByEntityId.GetWritable(entityId);
        if (memberTierIndex != NotAMember)
        {
            return;
        }

        var tierIndex = _currentTierIndexLookup(entityId);
        memberTierIndex = tierIndex;
        _tierBuckets[tierIndex].OnEntityAdded(entityId);
    }

    /// <summary>Removes an entity from their assigned tier and bucket.</summary>
    /// <param name="entityId">The ID of the entity to remove.</param>
    public void OnMemberRemoved(int entityId)
    {
        var tierIndex = _memberTierIndexByEntityId.Get(entityId);
        if (tierIndex == NotAMember)
        {
            return;
        }

        _memberTierIndexByEntityId.GetWritable(entityId) = NotAMember;
        _tierBuckets[tierIndex].OnEntityRemoved(entityId);
    }

    /// <summary>Updates the tier assignment for an entity that has moved to a different tier.</summary>
    /// <param name="entityId">The ID of the entity to update.</param>
    /// <param name="newTierIndex">The new tier index for the entity.</param>
    public void OnEntityTierChanged(int entityId, byte newTierIndex)
    {
        var oldTierIndex = _memberTierIndexByEntityId.Get(entityId);
        if (oldTierIndex == NotAMember || oldTierIndex == newTierIndex)
        {
            return;
        }

        _tierBuckets[oldTierIndex].OnEntityRemoved(entityId);
        _tierBuckets[newTierIndex].OnEntityAdded(entityId);
        _memberTierIndexByEntityId.GetWritable(entityId) = newTierIndex;
    }

    /// <summary>How many tiers this set was constructed with.</summary>
    public int TierCount => _tierBuckets.Length;

    /// <summary>Gets the bucket to process for a specific tier at the given frame count.</summary>
    /// <param name="tierIndex">The index of the tier.</param>
    /// <param name="frameCount">The frame count.</param>
    /// <returns>The bucket for the specified tier and frame count.</returns>
    public ReadOnlySpan<int> GetTierBucket(int tierIndex, long frameCount)
    {
        var bucket = _tierBuckets[tierIndex];
        return bucket.GetBucket((ushort)(frameCount % bucket.StripeCount));
    }

    /// <summary>Gets the number of frames between visits for a specific tier.</summary>
    /// <param name="tierIndex">The index of the tier.</param>
    /// <returns>The number of frames per visit for the specified tier.</returns>
    public ushort GetTierFramesPerVisit(int tierIndex) => _tierBuckets[tierIndex].StripeCount;
}
