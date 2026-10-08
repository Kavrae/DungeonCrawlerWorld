using System.Diagnostics;
using Engine.Math;
using Game.Modules.Auras;
using Game.World;

namespace Tests.Modules.Auras;

/// <summary>A reach-4 and a reach-30 source walking back and forth across a neighborhood seam, rewriting both diamonds every step as a move does.</summary>
/// <remarks>
/// Asserts a ratio, not a time (see AbilityScorePerformanceTests): the cost per cell written must not
/// grow with the source's size, which is what dense chunks are for -- a large source pays for its
/// cells, never for chunks or neighborhoods being found, made or freed along the way. And once a
/// walk has been made, walking it again allocates nothing: freed chunks are reused.
/// </remarks>
[TestClass]
[TestCategory("Performance")]
public sealed class AuraTotalsPerformanceTests
{
    private const byte AuraId = 0;
    private const int SmallReach = 4;
    private const int LargeReach = 30;
    private const int WalkHalfLength = 48;
    private const int Repetitions = 5;

    /// <summary>Largest acceptable (time per cell, reach 30) / (time per cell, reach 4). A large splat amortises its fixed costs better, so this is about 1 when per-cell cost is flat.</summary>
    private const double MaxPerCellCostRatio = 2;

    private static readonly int SeamX = Neighborhoods.SizeTiles;

    [TestMethod]
    public void SourceWalkingAcrossANeighborhoodSeam_CostsTheSamePerCellWhateverItsReach()
    {
        MeasureWalk(SmallReach, walks: 2);
        MeasureWalk(LargeReach, walks: 2);

        var smallNanosecondsPerCell = double.MaxValue;
        var largeNanosecondsPerCell = double.MaxValue;
        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            smallNanosecondsPerCell = System.Math.Min(smallNanosecondsPerCell, MeasureWalk(SmallReach, walks: 400).NanosecondsPerCell);
            largeNanosecondsPerCell = System.Math.Min(largeNanosecondsPerCell, MeasureWalk(LargeReach, walks: 10).NanosecondsPerCell);
        }

        var ratio = largeNanosecondsPerCell / smallNanosecondsPerCell;
        Console.WriteLine($"Reach {SmallReach}: {smallNanosecondsPerCell:F2} ns/cell; reach {LargeReach}: {largeNanosecondsPerCell:F2} ns/cell; ratio {ratio:F2}");
        Assert.IsLessThanOrEqualTo(MaxPerCellCostRatio, ratio,
            $"Reach {LargeReach} costs {largeNanosecondsPerCell:F2} ns per cell written against {smallNanosecondsPerCell:F2} for reach {SmallReach}: per-cell cost grows with reach.");
    }

    [TestMethod]
    public void SourceWalkingAcrossANeighborhoodSeam_AllocatesNothingOnceWalked()
    {
        foreach (var reach in new[] { SmallReach, LargeReach })
        {
            var result = MeasureWalk(reach, walks: 3);

            Assert.AreEqual(0L, result.BytesAllocatedAfterFirstWalk, $"Reach {reach}: walking the same path again allocated.");
            Assert.AreEqual(0, result.ChunksLeft, $"Reach {reach}: chunks left after the source was removed.");
        }
    }

    private readonly record struct WalkResult(double NanosecondsPerCell, long BytesAllocatedAfterFirstWalk, int ChunksLeft);

    /// <summary>Walks a source of the given reach from WalkHalfLength tiles west of the seam to as far east and back, walks times; the first walk is untimed.</summary>
    private static WalkResult MeasureWalk(int reach, int walks)
    {
        var world = TestWorlds.Create(Map.Unbounded(depth: 1));
        var grid = new AuraGrid(world);
        const int power = 8;
        var cellsPerSplat = 2 * reach * (reach + 1) + 1;
        var position = new Vector3Int(SeamX - WalkHalfLength, 500, 0);
        grid.AddSource(position, power, reach, AuraFalloff.Linear, AuraId);

        void Walk()
        {
            for (var direction = 1; direction >= -1; direction -= 2)
            {
                for (var step = 0; step < 2 * WalkHalfLength; step++)
                {
                    var next = position with { X = position.X + direction };
                    grid.RemoveSource(position, power, reach, AuraFalloff.Linear, AuraId);
                    grid.AddSource(next, power, reach, AuraFalloff.Linear, AuraId);
                    position = next;
                }
            }
        }

        Walk();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startTimestamp = Stopwatch.GetTimestamp();
        for (var walk = 1; walk < walks; walk++)
        {
            Walk();
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        grid.RemoveSource(position, power, reach, AuraFalloff.Linear, AuraId);

        var cellsWritten = (double)(walks - 1) * 4 * WalkHalfLength * 2 * cellsPerSplat;
        return new WalkResult(elapsed.TotalNanoseconds / cellsWritten, allocated, grid.TotalsChunkCount);
    }
}
