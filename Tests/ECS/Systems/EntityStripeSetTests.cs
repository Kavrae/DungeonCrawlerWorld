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
    private const int B = EntityStripeSet.BlockSize;

    [TestMethod]
    public void Constructor_SeedsFromExistingEntityIds()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, B, B + 1, 2 * B, 3 * B]);

        CollectionAssert.AreEquivalent(new[] { 0, 1, 3 * B }, stripeSet.GetBucket(0).ToArray());
        CollectionAssert.AreEquivalent(new[] { B, B + 1 }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2 * B }, stripeSet.GetBucket(2).ToArray());
    }

    [TestMethod]
    public void OnEntityAdded_AssignsByIdBlockRoundRobin()
    {
        var stripeSet = new EntityStripeSet(3, []);

        stripeSet.OnEntityAdded(7 * B + 5);

        CollectionAssert.AreEquivalent(new[] { 7 * B + 5 }, stripeSet.GetBucket(1).ToArray());
    }

    [TestMethod]
    public void OnEntityAdded_ABlockSharesOneStripe()
    {
        var stripeSet = new EntityStripeSet(3, []);

        for (var entityId = 2 * B; entityId < 3 * B; entityId++)
        {
            stripeSet.OnEntityAdded(entityId);
        }

        Assert.AreEqual(0, stripeSet.GetBucket(0).Length);
        Assert.AreEqual(0, stripeSet.GetBucket(1).Length);
        Assert.AreEqual(B, stripeSet.GetBucket(2).Length);
    }

    [TestMethod]
    public void OnEntityRemoved_OnlyAffectsTheRemovedEntitysOwnBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, 1, B, B + 1, 2 * B, 2 * B + 1, 3 * B]);

        stripeSet.OnEntityRemoved(0);

        CollectionAssert.AreEquivalent(new[] { 1, 3 * B }, stripeSet.GetBucket(0).ToArray());
        // Neither sibling bucket was touched by a removal from bucket 0's own internal swap.
        CollectionAssert.AreEquivalent(new[] { B, B + 1 }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2 * B, 2 * B + 1 }, stripeSet.GetBucket(2).ToArray());
    }

    [TestMethod]
    public void OnEntityRemoved_ThenReAdded_AssignsBackToItsOwnBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, B, 2 * B, 3 * B]);

        stripeSet.OnEntityRemoved(3 * B);
        stripeSet.OnEntityAdded(3 * B);

        CollectionAssert.AreEquivalent(new[] { 0, 3 * B }, stripeSet.GetBucket(0).ToArray());
    }

    [TestMethod]
    public void OnEntityRemoved_Unknown_DoesNotThrowOrAffectAnyBucket()
    {
        var stripeSet = new EntityStripeSet(3, [0, B, 2 * B]);

        stripeSet.OnEntityRemoved(999 * B);

        CollectionAssert.AreEquivalent(new[] { 0 }, stripeSet.GetBucket(0).ToArray());
        CollectionAssert.AreEquivalent(new[] { B }, stripeSet.GetBucket(1).ToArray());
        CollectionAssert.AreEquivalent(new[] { 2 * B }, stripeSet.GetBucket(2).ToArray());
    }

    /// <summary>
    /// The exact scenario that broke dense-index striding: heavy churn (repeated add/remove,
    /// including removing entities the swap-with-last logic would have relocated under the
    /// old scheme) followed by a full audit that every currently-live entity appears in
    /// exactly one bucket, and every bucket's entities are still where EntityStripeSet.StripeOf
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
                Assert.AreEqual((int)stripe, EntityStripeSet.StripeOf(entityId, stripeCount), $"Entity {entityId} is in bucket {stripe} but should be in {EntityStripeSet.StripeOf(entityId, stripeCount)}.");
                Assert.IsTrue(seenEntityIds.Add(entityId), $"Entity {entityId} appears in more than one bucket.");
            }
        }

        CollectionAssert.AreEquivalent(liveEntityIds.ToArray(), seenEntityIds.ToArray());
    }
}