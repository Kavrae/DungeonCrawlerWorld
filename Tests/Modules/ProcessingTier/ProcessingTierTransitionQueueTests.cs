using Engine.Math;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class ProcessingTierTransitionQueueTests
{
    private const int ThawingEntityId = 1;
    private const int FreezingEntityId = 2;

    private static (ProcessingTierTransitionQueue Queue, NeighborhoodMembershipIndex Membership) Build()
    {
        var membership = new NeighborhoodMembershipIndex();
        membership.Set(ThawingEntityId, new Vector3Int(10, 10, 0));
        membership.Set(FreezingEntityId, new Vector3Int(Neighborhoods.SizeTiles + 10, 10, 0));

        var queue = new ProcessingTierTransitionQueue();
        queue.Enqueue(0, 0, 0, ProcessingTierTransitionBand.Thawing);
        queue.Enqueue(1, 0, 0, ProcessingTierTransitionBand.Freezing);
        return (queue, membership);
    }

    private static List<int> DrainAll(ProcessingTierTransitionQueue queue, NeighborhoodMembershipIndex membership, bool thawingHeld)
    {
        var drained = new List<int>();
        while (queue.TryDequeue(membership, out var entityId, thawingHeld))
        {
            drained.Add(entityId);
        }

        return drained;
    }

    [TestMethod]
    public void TryDequeue_ThawBandFirst()
    {
        var (queue, membership) = Build();

        CollectionAssert.AreEqual(new[] { ThawingEntityId, FreezingEntityId }, DrainAll(queue, membership, thawingHeld: false));
    }

    [TestMethod]
    public void TryDequeue_DrainsBandsInOrder_WhateverOrderTheyWereQueuedIn()
    {
        const int UnsimulatedEntityId = 3;
        var membership = new NeighborhoodMembershipIndex();
        membership.Set(ThawingEntityId, new Vector3Int(10, 10, 0));
        membership.Set(FreezingEntityId, new Vector3Int(Neighborhoods.SizeTiles + 10, 10, 0));
        membership.Set(UnsimulatedEntityId, new Vector3Int(2 * Neighborhoods.SizeTiles + 10, 10, 0));

        var queue = new ProcessingTierTransitionQueue();
        queue.Enqueue(2, 0, 0, ProcessingTierTransitionBand.Unsimulated);
        queue.Enqueue(1, 0, 0, ProcessingTierTransitionBand.Freezing);
        queue.Enqueue(0, 0, 0, ProcessingTierTransitionBand.Thawing);

        CollectionAssert.AreEqual(new[] { ThawingEntityId, FreezingEntityId, UnsimulatedEntityId }, DrainAll(queue, membership, thawingHeld: false));
    }

    [TestMethod]
    public void HasPendingSimulatedChanges_TurnsFalseOnceOnlyTheUnsimulatedBandIsLeft()
    {
        var membership = new NeighborhoodMembershipIndex();
        membership.Set(FreezingEntityId, new Vector3Int(10, 10, 0));
        membership.Set(3, new Vector3Int(Neighborhoods.SizeTiles + 10, 10, 0));
        membership.Set(4, new Vector3Int(Neighborhoods.SizeTiles + 20, 10, 0));
        var queue = new ProcessingTierTransitionQueue();
        queue.Enqueue(0, 0, 0, ProcessingTierTransitionBand.Freezing);
        queue.Enqueue(1, 0, 0, ProcessingTierTransitionBand.Unsimulated);

        Assert.IsTrue(queue.HasPendingSimulatedChanges);
        Assert.IsTrue(queue.TryDequeue(membership, out _));
        Assert.IsFalse(queue.HasPendingSimulatedChanges, "The freeze band's only entity is taken.");

        Assert.IsTrue(queue.TryDequeue(membership, out _));
        Assert.IsFalse(queue.HasPendingSimulatedChanges);
        Assert.IsTrue(queue.HasPending, "One unsimulated entity is still waiting.");
    }

    [TestMethod]
    public void TryDequeue_ThawingHeld_DrainsTheOtherBandAndKeepsTheThawBandQueued()
    {
        var (queue, membership) = Build();

        CollectionAssert.AreEqual(new[] { FreezingEntityId }, DrainAll(queue, membership, thawingHeld: true));
        Assert.IsTrue(queue.HasPending);

        CollectionAssert.AreEqual(new[] { ThawingEntityId }, DrainAll(queue, membership, thawingHeld: false));
        Assert.IsFalse(queue.HasPending);
    }

    [TestMethod]
    public void TryDequeue_ThawingHeldMidNeighborhood_FinishesTheOneAlreadyStarted()
    {
        var (queue, membership) = Build();
        membership.Set(3, new Vector3Int(20, 20, 0));
        Assert.IsTrue(queue.TryDequeue(membership, out var first));

        Assert.IsTrue(queue.TryDequeue(membership, out var second, thawingHeld: true));

        CollectionAssert.AreEquivalent(new[] { ThawingEntityId, 3 }, new[] { first, second });
    }
}
