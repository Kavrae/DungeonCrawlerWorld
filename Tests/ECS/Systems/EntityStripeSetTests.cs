using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

/// <summary>
/// Structural correctness for the fix to the churn bug discussed alongside decision #11:
/// PackedComponentPool.Remove reassigns dense indices via swap-with-last, so a stripe
/// scheme keyed on live dense index could see the same physical entity land in a
/// not-yet-visited bucket after an unrelated removal (double-processed this cycle) or in an
/// already-visited bucket (skipped until next cycle). EntityStripeSet buckets by entityId
/// instead, which is fixed for an entity's whole lifetime -- these tests prove a removal
/// only ever perturbs the ONE bucket the removed entity belonged to, never any other.
/// </summary>
[TestClass]
public sealed class EntityStripeSetTests
{
    [TestMethod]
    public void Constructor_SeedsFromExistingEntityIds()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, 2, 3, 4, 5]);

        CollectionAssert.AreEquivalent(new[] { 0, 3 }, stripeSet.GetBucket(0).ToArray());
        CollectionAssert.AreEquivalent(new[] { 1, 4 }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2, 5 }, stripeSet.GetBucket(2).ToArray());
    }

    [TestMethod]
    public void OnEntityAdded_AssignsByEntityIdModuloStripeCount()
    {
        var stripeSet = new EntityStripeSet(3, []);

        stripeSet.OnEntityAdded(7);

        CollectionAssert.AreEquivalent(new[] { 7 }, stripeSet.GetBucket(1).ToArray());
    }

    [TestMethod]
    public void OnEntityRemoved_OnlyAffectsTheRemovedEntitysOwnBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        // Bucket 0: {0,3,6,9}. Bucket 1: {1,4,7}. Bucket 2: {2,5,8}.

        stripeSet.OnEntityRemoved(0);

        CollectionAssert.AreEquivalent(new[] { 3, 6, 9 }, stripeSet.GetBucket(0).ToArray());
        // Neither sibling bucket was touched by a removal from bucket 0's own internal swap.
        CollectionAssert.AreEquivalent(new[] { 1, 4, 7 }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2, 5, 8 }, stripeSet.GetBucket(2).ToArray());
    }

    [TestMethod]
    public void OnEntityRemoved_ThenReAdded_AssignsBackToItsOwnBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, 2, 3]);

        stripeSet.OnEntityRemoved(3);
        stripeSet.OnEntityAdded(3);

        CollectionAssert.AreEquivalent(new[] { 0, 3 }, stripeSet.GetBucket(0).ToArray());
    }

    [TestMethod]
    public void OnEntityRemoved_Unknown_DoesNotThrowOrAffectAnyBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, 2]);

        stripeSet.OnEntityRemoved(999);

        CollectionAssert.AreEquivalent(new[] { 0 }, stripeSet.GetBucket(0).ToArray());
        CollectionAssert.AreEquivalent(new[] { 1 }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2 }, stripeSet.GetBucket(2).ToArray());
    }

    /// <summary>
    /// The exact scenario that broke dense-index striding: heavy churn (repeated add/remove,
    /// including removing entities the swap-with-last logic would have relocated under the
    /// old scheme) followed by a full audit that every currently-live entity appears in
    /// exactly one bucket, and every bucket's entities are still where entityId % StripeCount
    /// says they should be.
    /// </summary>
    [TestMethod]
    public void HeavyChurn_EveryLiveEntityEndsUpInExactlyOneCorrectBucket()
    {
        const byte stripeCount = 7;
        var stripeSet = new EntityStripeSet(stripeCount, []);
        var liveEntityIds = new HashSet<int>();

        var random = new Random(12345);
        for (var entityId = 0; entityId < 500; entityId++)
        {
            stripeSet.OnEntityAdded(entityId);
            liveEntityIds.Add(entityId);
        }

        for (var operation = 0; operation < 2000; operation++)
        {
            if (liveEntityIds.Count > 0 && random.Next(2) == 0)
            {
                var toRemove = liveEntityIds.ElementAt(random.Next(liveEntityIds.Count));
                stripeSet.OnEntityRemoved(toRemove);
                liveEntityIds.Remove(toRemove);
            }
            else
            {
                var newEntityId = 500 + operation;
                stripeSet.OnEntityAdded(newEntityId);
                liveEntityIds.Add(newEntityId);
            }
        }

        var seenEntityIds = new HashSet<int>();
        for (byte stripe = 0; stripe < stripeCount; stripe++)
        {
            foreach (var entityId in stripeSet.GetBucket(stripe))
            {
                Assert.AreEqual((int)stripe, entityId % stripeCount, $"Entity {entityId} is in bucket {stripe} but should be in {entityId % stripeCount}.");
                Assert.IsTrue(seenEntityIds.Add(entityId), $"Entity {entityId} appears in more than one bucket.");
            }
        }

        CollectionAssert.AreEquivalent(liveEntityIds.ToArray(), seenEntityIds.ToArray());
    }
}