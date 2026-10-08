using Engine.Math;
using Game.Modules.Auras;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraTotalsTests
{
    private const byte FirstAuraId = 0;
    private const byte SecondAuraId = 7;

    [TestMethod]
    public void Get_NothingAdded_IsZeroEverywhere()
    {
        var totals = new AuraTotals(depth: 3);

        Assert.AreEqual(0, totals.Get(new Vector3Int(0, 0, 0), FirstAuraId));
        Assert.AreEqual(0, totals.Get(new Vector3Int(-5000, 7000, 2), FirstAuraId));
    }

    [TestMethod]
    public void Add_IsReadBackOnlyAtThatCellLayerAndAura()
    {
        var totals = new AuraTotals(depth: 3);
        var cell = new Vector3Int(31, 31, 1);

        totals.Add(cell, FirstAuraId, 5);
        totals.Add(cell, FirstAuraId, 3);

        Assert.AreEqual(8, totals.Get(cell, FirstAuraId));
        Assert.AreEqual(0, totals.Get(cell, SecondAuraId));
        Assert.AreEqual(0, totals.Get(new Vector3Int(31, 31, 0), FirstAuraId), "The same cell on another layer is its own total.");
        Assert.AreEqual(0, totals.Get(new Vector3Int(32, 31, 1), FirstAuraId), "The next cell east is in the next chunk.");
        Assert.AreEqual(0, totals.Get(new Vector3Int(31, 32, 1), FirstAuraId), "The next cell south is in the next chunk.");
    }

    [TestMethod]
    public void Add_AcrossNeighborhoodAndNegativeEdges_KeepsEveryCellApart()
    {
        var totals = new AuraTotals(depth: 1);
        Vector3Int[] cells = [new(-1, -1, 0), new(0, 0, 0), new(-1, 0, 0), new(0, -1, 0), new(1023, 1023, 0), new(1024, 1024, 0), new(-1024, -1024, 0), new(-1025, 5, 0)];

        for (var index = 0; index < cells.Length; index++)
        {
            totals.Add(cells[index], FirstAuraId, index + 1);
        }

        for (var index = 0; index < cells.Length; index++)
        {
            Assert.AreEqual(index + 1, totals.Get(cells[index], FirstAuraId), $"Cell {cells[index]}");
        }
    }

    [TestMethod]
    public void Add_ReportsCrossingZeroEachWay()
    {
        var totals = new AuraTotals(depth: 1);
        var cell = new Vector3Int(4, 4, 0);

        Assert.AreEqual(AuraTotals.CellChange.BecameNonZero, totals.Add(cell, FirstAuraId, 4));
        Assert.AreEqual(AuraTotals.CellChange.Unchanged, totals.Add(cell, FirstAuraId, 2));
        Assert.AreEqual(AuraTotals.CellChange.Unchanged, totals.Add(cell, FirstAuraId, -2));
        Assert.AreEqual(AuraTotals.CellChange.BecameZero, totals.Add(cell, FirstAuraId, -4));
        Assert.AreEqual(AuraTotals.CellChange.Unchanged, totals.Add(cell, FirstAuraId, 0));
    }

    [TestMethod]
    public void Add_BackToZero_FreesTheChunkOnlyWhenItsLastCellEmpties()
    {
        var totals = new AuraTotals(depth: 1);
        var first = new Vector3Int(1, 1, 0);
        var second = new Vector3Int(2, 1, 0);
        totals.Add(first, FirstAuraId, 3);
        totals.Add(second, FirstAuraId, 3);
        Assert.AreEqual(1, totals.ChunkCount);

        totals.Add(first, FirstAuraId, -3);
        Assert.AreEqual(1, totals.ChunkCount, "The chunk still holds second.");

        totals.Add(second, FirstAuraId, -3);
        Assert.AreEqual(0, totals.ChunkCount);
        Assert.AreEqual(0, totals.NeighborhoodCount);
        Assert.AreEqual(0, totals.Get(second, FirstAuraId));
    }

    [TestMethod]
    public void Add_EachAuraAndNeighborhoodHoldsItsOwnChunks_AllFreedWhenEmptied()
    {
        var totals = new AuraTotals(depth: 2);
        Vector3Int[] cells = [new(10, 10, 0), new(10, 10, 1), new(2000, 10, 0), new(-10, -10, 1)];
        foreach (var cell in cells)
        {
            totals.Add(cell, FirstAuraId, 1);
            totals.Add(cell, SecondAuraId, 1);
        }

        Assert.AreEqual(8, totals.ChunkCount);
        Assert.AreEqual(6, totals.NeighborhoodCount, "Three neighborhoods, each held once per aura.");

        foreach (var cell in cells)
        {
            totals.Add(cell, FirstAuraId, -1);
            totals.Add(cell, SecondAuraId, -1);
        }

        Assert.AreEqual(0, totals.ChunkCount);
        Assert.AreEqual(0, totals.NeighborhoodCount);
    }

    [TestMethod]
    public void Add_AfterAChunkWasFreed_StartsFromZeroAgain()
    {
        var totals = new AuraTotals(depth: 1);
        var cell = new Vector3Int(100, 100, 0);
        totals.Add(cell, FirstAuraId, 9);
        totals.Add(cell, FirstAuraId, -9);

        totals.Add(new Vector3Int(500, 500, 0), SecondAuraId, 2);

        Assert.AreEqual(2, totals.Get(new Vector3Int(500, 500, 0), SecondAuraId));
        Assert.AreEqual(0, totals.Get(new Vector3Int(100, 100, 0), SecondAuraId), "A reused chunk carries nothing from before.");
    }

    [TestMethod]
    public void AnyOtherAuraHas_IgnoresTheAuraAskedAbout()
    {
        var totals = new AuraTotals(depth: 1);
        var cell = new Vector3Int(3, 3, 0);
        totals.Add(cell, FirstAuraId, 1);

        Assert.IsFalse(totals.AnyOtherAuraHas(cell, FirstAuraId));
        Assert.IsTrue(totals.AnyOtherAuraHas(cell, SecondAuraId));
    }

    [TestMethod]
    public void AllocatedBytes_CoversChunksInUseAndKeptForReuse()
    {
        var totals = new AuraTotals(depth: 1);
        Assert.AreEqual(0, totals.AllocatedBytes);

        totals.Add(new Vector3Int(0, 0, 0), FirstAuraId, 1);
        var inUse = totals.AllocatedBytes;
        Assert.IsGreaterThan(0L, inUse);

        totals.Add(new Vector3Int(0, 0, 0), FirstAuraId, -1);
        Assert.AreEqual(inUse, totals.AllocatedBytes, "A freed chunk and slot array are kept for the next one.");
    }
}
