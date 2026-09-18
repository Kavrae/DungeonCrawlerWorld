using Engine.Math;
using Game.Modules.ProcessingTier;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class NeighborhoodMembershipIndexTests
{
    private static List<int> Cell(NeighborhoodMembershipIndex index, int cellX, int cellY, int z)
    {
        var entityIds = new List<int>();
        index.CopyCell(cellX, cellY, z, entityIds);
        return entityIds;
    }

    [TestMethod]
    public void Set_NewEntity_IsIndexedInItsCell()
    {
        var index = new NeighborhoodMembershipIndex();

        index.Set(3, new Vector3Int(1500, 250, 1));

        CollectionAssert.AreEquivalent(new[] { 3 }, Cell(index, 1, 0, 1));
        Assert.AreEqual(1, index.Count);
    }

    [TestMethod]
    public void Set_SameCellTwice_IndexesOnce()
    {
        var index = new NeighborhoodMembershipIndex();

        index.Set(3, new Vector3Int(1500, 250, 1));
        index.Set(3, new Vector3Int(1600, 900, 1));

        CollectionAssert.AreEquivalent(new[] { 3 }, Cell(index, 1, 0, 1));
        Assert.AreEqual(1, index.Count);
    }

    [TestMethod]
    public void Set_DifferentCell_MovesEntityOutOfOldCell()
    {
        var index = new NeighborhoodMembershipIndex();
        index.Set(3, new Vector3Int(1500, 250, 1));

        index.Set(3, new Vector3Int(2500, 250, 1));

        Assert.IsEmpty(Cell(index, 1, 0, 1));
        CollectionAssert.AreEquivalent(new[] { 3 }, Cell(index, 2, 0, 1));
        Assert.AreEqual(1, index.Count);
    }

    [TestMethod]
    public void Set_SameXYDifferentLayer_IsADifferentCell()
    {
        var index = new NeighborhoodMembershipIndex();
        index.Set(3, new Vector3Int(500, 500, 0));

        index.Set(3, new Vector3Int(500, 500, 2));

        Assert.IsEmpty(Cell(index, 0, 0, 0));
        CollectionAssert.AreEquivalent(new[] { 3 }, Cell(index, 0, 0, 2));
    }

    /// <summary>Moving an entity out of the middle of a cell's list swap-removes it; the entity swapped into its slot must still be removable afterwards.</summary>
    [TestMethod]
    public void Set_MovesMiddleEntity_RemainingEntitiesStayConsistent()
    {
        var index = new NeighborhoodMembershipIndex();
        index.Set(1, new Vector3Int(10, 10, 0));
        index.Set(2, new Vector3Int(20, 20, 0));
        index.Set(3, new Vector3Int(30, 30, 0));

        index.Set(1, new Vector3Int(1500, 10, 0));
        index.Remove(3);

        CollectionAssert.AreEquivalent(new[] { 2 }, Cell(index, 0, 0, 0));
        CollectionAssert.AreEquivalent(new[] { 1 }, Cell(index, 1, 0, 0));
        Assert.AreEqual(2, index.Count);
    }

    [TestMethod]
    public void Remove_IndexedEntity_LeavesCellAndCount()
    {
        var index = new NeighborhoodMembershipIndex();
        index.Set(3, new Vector3Int(1500, 250, 1));

        index.Remove(3);

        Assert.IsEmpty(Cell(index, 1, 0, 1));
        Assert.AreEqual(0, index.Count);
        Assert.IsFalse(index.IsIndexedAt(3, new Vector3Int(1500, 250, 1)));
    }

    [TestMethod]
    public void Remove_NeverIndexedEntity_IsNoOp()
    {
        var index = new NeighborhoodMembershipIndex();

        index.Remove(3);
        index.Remove(1_000_000);

        Assert.AreEqual(0, index.Count);
    }

    [TestMethod]
    public void Set_LargeEntityId_GrowsStorage()
    {
        var index = new NeighborhoodMembershipIndex();

        index.Set(2_500_000, new Vector3Int(10, 10, 0));

        Assert.IsTrue(index.IsIndexedAt(2_500_000, new Vector3Int(999, 999, 0)));
    }

    [TestMethod]
    public void Set_NegativeCell_IsDistinctFromCellZero()
    {
        var index = new NeighborhoodMembershipIndex();

        index.Set(1, new Vector3Int(-1, 5, 0));
        index.Set(2, new Vector3Int(5, 5, 0));

        CollectionAssert.AreEquivalent(new[] { 1 }, Cell(index, -1, 0, 0));
        CollectionAssert.AreEquivalent(new[] { 2 }, Cell(index, 0, 0, 0));
    }

    [TestMethod]
    public void CopyCell_AppendsRatherThanReplacing()
    {
        var index = new NeighborhoodMembershipIndex();
        index.Set(1, new Vector3Int(10, 10, 0));
        index.Set(2, new Vector3Int(1100, 10, 0));

        var entityIds = new List<int>();
        index.CopyCell(0, 0, 0, entityIds);
        index.CopyCell(1, 0, 0, entityIds);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, entityIds);
    }
}
