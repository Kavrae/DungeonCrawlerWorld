using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class LeakEvaluationTests
{
    private const int SampleCount = 12;

    private readonly struct HeldByEveryEntity;

    private readonly struct HeldByBuiltEntities;

    private static readonly EntityPopulationPolicy BuiltPopulation = new("Built", static () => 0, static componentType => componentType == typeof(HeldByEveryEntity));

    /// <summary>A history whose every figure moves linearly from its first value to its second across SampleCount samples.</summary>
    private static List<LeakSample> History(
        (int First, int Last) living,
        (int First, int Last) built,
        (long First, long Last) heapBytes,
        (int First, int Last) heldByEveryEntity,
        (int First, int Last) heldByBuiltEntities)
    {
        static double Step((double First, double Last) range, int sampleIndex) =>
            range.First + (range.Last - range.First) * sampleIndex / (SampleCount - 1);

        var history = new List<LeakSample>();
        for (var sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
        {
            history.Add(new LeakSample(
                DateTime.UnixEpoch.AddSeconds(5 * sampleIndex),
                (long)Step(heapBytes, sampleIndex),
                0,
                0,
                0,
                (int)Step(living, sampleIndex),
                (int)Step(built, sampleIndex),
                new Dictionary<Type, int>
                {
                    [typeof(HeldByEveryEntity)] = (int)Step(heldByEveryEntity, sampleIndex),
                    [typeof(HeldByBuiltEntities)] = (int)Step(heldByBuiltEntities, sampleIndex),
                }));
        }

        return history;
    }

    private static List<LeakFinding> Evaluate(List<LeakSample> history, EntityPopulationPolicy? populations)
    {
        var findings = new List<LeakFinding>();
        LeakEvaluation.Evaluate(history, populations, findings);
        return findings;
    }

    private static List<LeakSample> PromotionShapedHistory() => History(
        living: (100_000, 100_000),
        built: (10_000, 40_000),
        heapBytes: (1_000_000_000, 1_300_000_000),
        heldByEveryEntity: (100_000, 100_000),
        heldByBuiltEntities: (10_000, 40_000));

    [TestMethod]
    public void APromotion_WithAPolicy_IsNoFinding()
    {
        Assert.IsEmpty(Evaluate(PromotionShapedHistory(), BuiltPopulation));
    }

    [TestMethod]
    public void APromotion_WithoutAPolicy_FlagsTheBuiltOnlyPoolAndTheHeap()
    {
        var subjects = Evaluate(PromotionShapedHistory(), populations: null).Select(static finding => finding.Subject).ToList();

        CollectionAssert.AreEquivalent(new[] { nameof(HeldByBuiltEntities), "Managed Heap" }, subjects);
    }

    [TestMethod]
    public void ABuiltOnlyPoolGrowing_WhileBuiltEntitiesStayFlat_IsFlaggedAgainstBuiltEntities()
    {
        var history = History(living: (100_000, 100_000), built: (10_000, 10_000), heapBytes: (1_000_000_000, 1_000_000_000), heldByEveryEntity: (100_000, 100_000), heldByBuiltEntities: (10_000, 40_000));

        var finding = Evaluate(history, BuiltPopulation).Single();

        Assert.AreEqual(nameof(HeldByBuiltEntities), finding.Subject);
        Assert.Contains("built entity count", finding.Detail);
    }

    [TestMethod]
    public void APoolEveryEntityHoldsGrowing_WhileLivingEntitiesStayFlat_IsFlaggedAgainstLivingEntities()
    {
        var history = History(living: (100_000, 100_000), built: (10_000, 40_000), heapBytes: (1_000_000_000, 1_000_000_000), heldByEveryEntity: (100_000, 400_000), heldByBuiltEntities: (10_000, 40_000));

        var finding = Evaluate(history, BuiltPopulation).Single();

        Assert.AreEqual(nameof(HeldByEveryEntity), finding.Subject);
        Assert.Contains("live entity count", finding.Detail);
    }

    [TestMethod]
    public void TheHeapGrowing_WhileBothPopulationsStayFlat_IsFlaggedNamingBoth()
    {
        var history = History(living: (100_000, 100_000), built: (10_000, 10_000), heapBytes: (1_000_000_000, 1_300_000_000), heldByEveryEntity: (100_000, 100_000), heldByBuiltEntities: (10_000, 10_000));

        var finding = Evaluate(history, BuiltPopulation).Single();

        Assert.AreEqual("Managed Heap", finding.Subject);
        Assert.Contains("live entity count", finding.Detail);
        Assert.Contains("built entity count", finding.Detail);
    }

    [TestMethod]
    public void FewerThanMinimumSamples_IsNoFinding()
    {
        var history = PromotionShapedHistory().Take(LeakEvaluation.MinimumSamplesForEvaluation - 1).ToList();

        Assert.IsEmpty(Evaluate(history, populations: null));
    }
}
