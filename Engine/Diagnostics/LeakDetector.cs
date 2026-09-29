using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Engine.Diagnostics;

/// <summary>Throttled sampler of GC/entity/component-pool trends, flagging symptoms that often indicate a leak.</summary>
/// <remarks>
/// Keeps a rolling history of LeakSamples and re-evaluates it with LeakEvaluation after each sample --
/// see LeakEvaluation for what counts as a symptom and why findings are only indicators.
///
/// Sampling GC.GetTotalMemory/CollectionCount and enumerating ComponentManager.AllPools (same
/// pattern as ComponentMemoryTracker) are each O(pools), not O(entity count), so a sample stays
/// cheap regardless of world size -- but still heavier than a single Stopwatch bracket, so Tick
/// only re-samples once SampleInterval has elapsed, not every frame.
/// </remarks>
/// <param name="populations">The session's population split, compared against by pool; null compares every pool against every living entity.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LeakDetector(EntityManager entityManager, ComponentManager componentManager, EntityPopulationPolicy? populations)
{
    private const int MaxHistorySamples = 12;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

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

        var componentCounts = new Dictionary<Type, int>();
        foreach (var pool in componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                componentCounts[pool.ComponentType] = memoryReportingPool.Count;
            }
        }

        var sample = new LeakSample(
            now,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            entityManager.LivingEntityCount,
            populations?.CountPartialPopulation(),
            componentCounts);

        if (_history.Count == MaxHistorySamples)
        {
            _history.RemoveAt(0);
        }

        _history.Add(sample);

        LeakEvaluation.Evaluate(_history, populations, _findings);
    }
}
