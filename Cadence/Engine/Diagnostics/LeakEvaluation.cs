namespace Engine.Diagnostics;

/// <summary>Flags leak symptoms in a history of LeakSamples: a managed heap growing while the entities stay flat, or a pool growing faster than the entities that can hold it.</summary>
/// <remarks>
/// This is a heuristic indicator, not proof -- a flag here means "worth investigating with a real
/// profiler (dotnet-gcdump, a memory snapshot diff)," not "confirmed leak." It compares the oldest
/// and newest sample in the history, and a finding also needs the growth to continue into the second
/// half of it (middle sample to newest). Oldest-to-newest alone can't tell a leak from a pool filling
/// up to its steady state: sampling starts before the first simulated frame, when every gameplay pool
/// (timers, exposures, corpses) is empty or nearly so, and each fills over the first seconds of play.
/// A pool that has reached its steady state stops growing; a leak keeps going.
///
/// A pool is compared against the population that can hold it. With an EntityPopulationPolicy, a
/// component type every entity can hold is compared against every living entity, and any other against
/// the policy's partial population only: otherwise the game's own population shifts (a neighborhood's
/// creatures being built while the living count stays flat) look exactly like a leak, and a real leak
/// hides inside them. The heap is shared by both populations, so its finding needs both to be flat.
///
/// One false-positive shape is structural, not just a threshold-tuning problem: event-marker
/// components added once to an entity that already existed (e.g. DeadComponent on a kill,
/// AchievementUnlockedComponent on an unlock) grow with *event* rate, not entity-count growth --
/// a burst of kills/unlocks legitimately outpaces entity count the same way an actual leak would.
/// MinimumInstanceCountForPoolFinding and the raised PoolOutpacesEntityGrowthThreshold below cut
/// down the noisiest case (small pools, brief bursts), confirmed against a real ~75s session that
/// flagged DeadComponent/AchievementUnlockedComponent/NonBlockingComponent growth from ordinary
/// kills and achievement unlocks -- but they can't eliminate this category entirely, since a slow
/// real leak in a marker-style component would look statistically identical over a long enough
/// window. Findings still need a human read, not just a threshold pass.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class LeakEvaluation
{
    /// <summary>How few samples a history may hold and still be evaluated.</summary>
    public const int MinimumSamplesForEvaluation = 6;

    private const double HeapGrowthThreshold = 0.10;
    private const double EntityCountFlatThreshold = 0.02;
    private const double PoolOutpacesEntityGrowthThreshold = 0.50;
    private const double RecentPoolGrowthThreshold = 0.10;
    private const int MinimumInstanceCountForPoolFinding = 100;

    private const string LivingPopulationDescription = "live entity count";

    /// <summary>Replaces findings with history's findings, descending by growth ratio; none before MinimumSamplesForEvaluation samples.</summary>
    /// <param name="history">Samples, oldest first.</param>
    /// <param name="populations">The session's population split; null compares every pool against every living entity.</param>
    public static void Evaluate(IReadOnlyList<LeakSample> history, EntityPopulationPolicy? populations, List<LeakFinding> findings)
    {
        findings.Clear();

        if (history.Count < MinimumSamplesForEvaluation)
        {
            return;
        }

        var oldest = history[0];
        var middle = history[history.Count / 2];
        var newest = history[^1];

        var livingGrowth = new PopulationGrowth(LivingPopulationDescription, oldest.LiveEntityCount, middle.LiveEntityCount, newest.LiveEntityCount);
        PopulationGrowth? partialGrowth = populations is not null && oldest.PartialPopulationCount is { } oldestPartial && middle.PartialPopulationCount is { } middlePartial && newest.PartialPopulationCount is { } newestPartial
            ? new PopulationGrowth($"{populations.PartialPopulationName.ToLowerInvariant()} entity count", oldestPartial, middlePartial, newestPartial)
            : null;

        AddHeapFinding(history.Count, oldest, middle, newest, livingGrowth, partialGrowth, findings);

        foreach (var (componentType, oldestCount) in oldest.ComponentCounts)
        {
            if (!newest.ComponentCounts.TryGetValue(componentType, out var newestCount) || newestCount <= oldestCount)
            {
                continue;
            }

            if (newestCount < MinimumInstanceCountForPoolFinding)
            {
                continue;
            }

            var population = partialGrowth is { } partial && !populations!.IsHeldByEveryEntity(componentType) ? partial : livingGrowth;
            var poolGrowthRatio = GrowthRatio(oldestCount, newestCount);
            var recentPoolGrowthRatio = middle.ComponentCounts.TryGetValue(componentType, out var middleCount)
                ? GrowthRatio(middleCount, newestCount)
                : poolGrowthRatio;
            if (poolGrowthRatio - population.GrowthRatio > PoolOutpacesEntityGrowthThreshold
                && recentPoolGrowthRatio - population.RecentGrowthRatio > RecentPoolGrowthThreshold)
            {
                findings.Add(new LeakFinding(
                    componentType.Name,
                    $"{componentType.Name} pool grew {poolGrowthRatio:P0} ({oldestCount:N0} -> {newestCount:N0}) while {population.Description} grew {population.GrowthRatio:P0} ({population.OldestCount:N0} -> {population.NewestCount:N0}) -- components may not be getting removed when their owning entity is.",
                    poolGrowthRatio - population.GrowthRatio));
            }
        }

        findings.Sort(static (a, b) => b.GrowthRatio.CompareTo(a.GrowthRatio));
    }

    private static void AddHeapFinding(int sampleCount, LeakSample oldest, LeakSample middle, LeakSample newest, PopulationGrowth livingGrowth, PopulationGrowth? partialGrowth, List<LeakFinding> findings)
    {
        var heapGrowthRatio = GrowthRatio(oldest.TotalManagedBytes, newest.TotalManagedBytes);
        var recentHeapGrowthRatio = GrowthRatio(middle.TotalManagedBytes, newest.TotalManagedBytes);
        if (heapGrowthRatio <= HeapGrowthThreshold || recentHeapGrowthRatio <= 0 || livingGrowth.GrowthRatio >= EntityCountFlatThreshold || partialGrowth is { GrowthRatio: >= EntityCountFlatThreshold })
        {
            return;
        }

        var partialDetail = partialGrowth is { } partial
            ? $" and {partial.Description} grew only {partial.GrowthRatio:P0} ({partial.OldestCount:N0} -> {partial.NewestCount:N0})"
            : string.Empty;
        findings.Add(new LeakFinding(
            "Managed Heap",
            $"Managed heap grew {heapGrowthRatio:P0} over the last {sampleCount} samples while {livingGrowth.Description} grew only {livingGrowth.GrowthRatio:P0} ({livingGrowth.OldestCount:N0} -> {livingGrowth.NewestCount:N0}){partialDetail}.",
            heapGrowthRatio));
    }

    private static double GrowthRatio(double oldValue, double newValue)
    {
        if (oldValue <= 0)
        {
            return newValue > 0 ? 1.0 : 0.0;
        }

        return (newValue - oldValue) / oldValue;
    }

    /// <summary>One population's growth across a history: overall (oldest to newest) and recent (middle to newest).</summary>
    private readonly record struct PopulationGrowth(string Description, int OldestCount, int MiddleCount, int NewestCount)
    {
        public double GrowthRatio => LeakEvaluation.GrowthRatio(OldestCount, NewestCount);

        public double RecentGrowthRatio => LeakEvaluation.GrowthRatio(MiddleCount, NewestCount);
    }
}
