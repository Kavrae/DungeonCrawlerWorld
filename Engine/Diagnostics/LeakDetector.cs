using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Engine.Diagnostics;

/// <summary>Throttled sampler of GC/entity/component-pool trends, flagging symptoms that often indicate a leak.</summary>
/// <remarks>
/// This is a heuristic indicator, not proof -- a flag here means "worth investigating with a real
/// profiler (dotnet-gcdump, a memory snapshot diff)," not "confirmed leak." It compares the
/// oldest and newest sample in a rolling history window: managed heap growing while live entity
/// count stays flat/shrinking, or a component pool's instance count growing faster than the
/// entity count around it (components not being removed when their owning entity is), are both
/// symptoms real leaks tend to produce -- but so can legitimate warmup/caching behavior, so
/// findings should be read as "look here," not "here's the bug."
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
///
/// Sampling GC.GetTotalMemory/CollectionCount and enumerating ComponentManager.AllPools (same
/// pattern as ComponentMemoryTracker) are each O(pools), not O(entity count), so a sample stays
/// cheap regardless of world size -- but still heavier than a single Stopwatch bracket, so Tick
/// only re-samples once SampleInterval has elapsed, not every frame.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LeakDetector(EntityManager entityManager, ComponentManager componentManager)
{
    private const int MaxHistorySamples = 12;
    private const int MinimumSamplesForEvaluation = 6;
    private const double HeapGrowthThreshold = 0.10;
    private const double EntityCountFlatThreshold = 0.02;
    private const double PoolOutpacesEntityGrowthThreshold = 0.50;
    private const int MinimumInstanceCountForPoolFinding = 100;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly EntityManager _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    private readonly ComponentManager _componentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));
    private readonly List<LeakSample> _history = [];
    private readonly List<LeakFinding> _findings = [];

    private DateTime _lastSampleUtc = DateTime.MinValue;

    /// <summary>Every sample currently in the rolling history window, oldest first.</summary>
    public IReadOnlyList<LeakSample> History => _history;

    /// <summary>Findings from the most recent evaluation, descending by growth ratio. Empty when nothing looked worth flagging, or before enough samples exist.</summary>
    public IReadOnlyList<LeakFinding> Findings => _findings;

    /// <summary>Re-samples GC/entity/component state if SampleInterval has elapsed since the last sample, then re-evaluates findings against the updated history.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSampleUtc < SampleInterval)
        {
            return;
        }

        _lastSampleUtc = now;

        var componentCounts = new Dictionary<string, int>();
        foreach (var pool in _componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                componentCounts[pool.ComponentType.Name] = memoryReportingPool.Count;
            }
        }

        var sample = new LeakSample(
            now,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            _entityManager.LivingEntityCount,
            componentCounts);

        if (_history.Count == MaxHistorySamples)
        {
            _history.RemoveAt(0);
        }

        _history.Add(sample);

        Evaluate();
    }

    private void Evaluate()
    {
        _findings.Clear();

        if (_history.Count < MinimumSamplesForEvaluation)
        {
            return;
        }

        var oldest = _history[0];
        var newest = _history[^1];

        var entityGrowthRatio = GrowthRatio(oldest.LiveEntityCount, newest.LiveEntityCount);

        var heapGrowthRatio = GrowthRatio(oldest.TotalManagedBytes, newest.TotalManagedBytes);
        if (heapGrowthRatio > HeapGrowthThreshold && entityGrowthRatio < EntityCountFlatThreshold)
        {
            _findings.Add(new LeakFinding(
                "Managed Heap",
                $"Managed heap grew {heapGrowthRatio:P0} over the last {_history.Count} samples while live entity count grew only {entityGrowthRatio:P0} ({oldest.LiveEntityCount:N0} -> {newest.LiveEntityCount:N0}).",
                heapGrowthRatio));
        }

        foreach (var (componentTypeName, oldestCount) in oldest.ComponentCounts)
        {
            if (!newest.ComponentCounts.TryGetValue(componentTypeName, out var newestCount) || newestCount <= oldestCount)
            {
                continue;
            }

            if (newestCount < MinimumInstanceCountForPoolFinding)
            {
                continue;
            }

            var poolGrowthRatio = GrowthRatio(oldestCount, newestCount);
            if (poolGrowthRatio - entityGrowthRatio > PoolOutpacesEntityGrowthThreshold)
            {
                _findings.Add(new LeakFinding(
                    componentTypeName,
                    $"{componentTypeName} pool grew {poolGrowthRatio:P0} ({oldestCount:N0} -> {newestCount:N0}) while live entity count grew {entityGrowthRatio:P0} -- components may not be getting removed when their owning entity is.",
                    poolGrowthRatio - entityGrowthRatio));
            }
        }

        _findings.Sort(static (a, b) => b.GrowthRatio.CompareTo(a.GrowthRatio));
    }

    private static double GrowthRatio(double oldValue, double newValue)
    {
        if (oldValue <= 0)
        {
            return newValue > 0 ? 1.0 : 0.0;
        }

        return (newValue - oldValue) / oldValue;
    }
}
