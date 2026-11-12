using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Engine.Diagnostics;

/// <summary>Throttled sampler of GC/entity/component-pool trends, flagging symptoms that often indicate a leak.</summary>
/// <remarks>
/// Keeps a rolling history of LeakSamples and re-evaluates it with LeakEvaluation after each sample --
/// see LeakEvaluation for what counts as a symptom and why findings are only indicators.
///
/// The heap figure is what the latest collection left live, not GC.GetTotalMemory, whose reading rises through every
/// gen-0 cycle and drops at each collection by more than a leak's threshold, depending only on when the sample landed.
///
/// Sampling the GC's figures and enumerating ComponentManager.AllPools (same
/// pattern as ComponentMemoryTracker) are each O(pools), not O(entity count), so a sample stays
/// cheap regardless of world size -- but still heavier than a single Stopwatch bracket, so Tick
/// only re-samples once its sample interval has elapsed, not every frame.
/// </remarks>
/// <param name="populations">The session's population split, compared against by pool; null compares every pool against every living entity.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LeakDetector(EntityManager entityManager, ComponentManager componentManager, EntityPopulationPolicy? populations)
{
    private const int MaxHistorySamples = 12;

    private static readonly long SampleIntervalTimestampTicks = System.Diagnostics.Stopwatch.Frequency * 5;

    private readonly List<LeakSample> _history = [];
    private readonly List<LeakFinding> _findings = [];

    private long _lastSampleTimestamp;
    private bool _hasSampled;

    /// <summary>Every sample currently in the rolling history window, oldest first.</summary>
    public IReadOnlyList<LeakSample> History => _history;

    /// <summary>Findings from the most recent evaluation, descending by growth ratio. Empty when nothing looked worth flagging, or before enough samples exist.</summary>
    public IReadOnlyList<LeakFinding> Findings => _findings;

    /// <summary>Re-samples GC/entity/component state if its sample interval has elapsed since the last sample, then re-evaluates findings against the updated history.</summary>
    public void Tick()
    {
        var nowTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_hasSampled && nowTimestamp - _lastSampleTimestamp < SampleIntervalTimestampTicks)
        {
            return;
        }

        _hasSampled = true;
        _lastSampleTimestamp = nowTimestamp;

        var componentCounts = new Dictionary<Type, int>();
        foreach (var pool in componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                componentCounts[pool.ComponentType] = memoryReportingPool.Count;
            }
        }

        var sample = new LeakSample(
            DateTime.UtcNow,
            HeapBytesAfterLastCollection(),
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

    /// <summary>The managed heap's live bytes as the latest collection left them; the current heap size before the first collection, when there is no such figure yet.</summary>
    private static long HeapBytesAfterLastCollection()
    {
        var lastCollection = GC.GetGCMemoryInfo(GCKind.Any);
        if (lastCollection.Index == 0)
        {
            return GC.GetTotalMemory(forceFullCollection: false);
        }

        return lastCollection.HeapSizeBytes - lastCollection.FragmentedBytes;
    }
}
