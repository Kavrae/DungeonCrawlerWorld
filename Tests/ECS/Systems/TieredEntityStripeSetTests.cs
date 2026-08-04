using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class TieredEntityStripeSetTests
{
    private static int[] Collect(TieredEntityStripeSet set, long frameCount)
    {
        var result = new List<int>();
        foreach (var entityId in set.GetDueEntities(frameCount))
        {
            result.Add(entityId);
        }

        return result.ToArray();
    }

    [TestMethod]
    public void Constructor_SeedsMembersViaLookupDelegate_GetDueEntitiesReflectsPerTierCadence()
    {
        // baseStripeCount 1, divisors [1, 2] -- tier 0 (entities 0,1) is due every frameCount;
        // tier 1 (entities 10,11) has its own internal stripeCount of 2, so only half its
        // members are due on any given frameCount, split by entityId % 2.
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [0, 1, 10, 11], entityId => entityId < 2 ? (byte)0 : (byte)1);

        CollectionAssert.AreEquivalent(new[] { 0, 1, 10 }, Collect(tieredSet, 0));
        CollectionAssert.AreEquivalent(new[] { 0, 1, 11 }, Collect(tieredSet, 1));
    }

    [TestMethod]
    public void OnMemberAdded_PlacesEntityIntoTierReportedByLookup()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [], entityId => entityId < 2 ? (byte)0 : (byte)1);

        tieredSet.OnMemberAdded(10);

        CollectionAssert.AreEquivalent(new[] { 10 }, Collect(tieredSet, 0));
        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 1));
    }

    [TestMethod]
    public void OnMemberRemoved_RemovesFromItsCurrentTier()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [0], entityId => 0);

        tieredSet.OnMemberRemoved(0);

        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 0));
    }

    [TestMethod]
    public void OnMemberRemoved_Unknown_DoesNotThrowOrAffectAnything()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [0], entityId => 0);

        tieredSet.OnMemberRemoved(999);

        CollectionAssert.AreEquivalent(new[] { 0 }, Collect(tieredSet, 0));
    }

    [TestMethod]
    public void OnEntityTierChanged_MigratesEntityToNewTiersBucket()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [0], entityId => 0);

        // Entity 0 moves from tier 0 (always due) to tier 1 (stripeCount 2; 0 % 2 == 0 -> due
        // only on even frameCount values).
        tieredSet.OnEntityTierChanged(0, 1);

        CollectionAssert.AreEquivalent(new[] { 0 }, Collect(tieredSet, 4));
        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 5));
    }

    [TestMethod]
    public void OnEntityTierChanged_SameTier_IsNoOp()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [0], entityId => 0);

        tieredSet.OnEntityTierChanged(0, 0);

        CollectionAssert.AreEquivalent(new[] { 0 }, Collect(tieredSet, 0));
    }

    /// <summary>A tier-change source fans out to every entity it tracks regardless of whether this particular TieredEntityStripeSet's own population includes it -- entities never added via OnMemberAdded must not silently start appearing just because some other population's entity happens to share an id.</summary>
    [TestMethod]
    public void OnEntityTierChanged_NonMember_DoesNotAddIt()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2], [], entityId => 0);

        tieredSet.OnEntityTierChanged(42, 1);

        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 0));
        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 1));
    }

    /// <summary>Exercises the chained enumerator advancing past an empty tier bucket in the middle of the sequence, not just at the ends.</summary>
    [TestMethod]
    public void GetDueEntities_ChainsAcrossMultipleTiers_SkippingEmptyOnesInTheMiddle()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 1, 1], [0, 20], entityId => entityId == 0 ? (byte)0 : (byte)2);

        CollectionAssert.AreEquivalent(new[] { 0, 20 }, Collect(tieredSet, 0));
    }

    [TestMethod]
    public void GetDueEntities_AllTiersEmpty_YieldsNothing()
    {
        var tieredSet = new TieredEntityStripeSet(1, [1, 2, 4], [], entityId => 0);

        CollectionAssert.AreEquivalent(Array.Empty<int>(), Collect(tieredSet, 0));
    }
}
