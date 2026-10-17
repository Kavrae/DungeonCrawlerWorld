using Engine.ECS.Components;

namespace Engine.Diagnostics;

/// <summary>Throttled sampler of per-component-type memory usage.</summary>
/// <remarks>
/// Enumerates ComponentManager.AllPools (the same enumeration ComponentInspector already uses),
/// filtering for IMemoryReportingComponentPool -- every concrete pool store implements it, so a
/// brand-new component type gets memory reporting for free, nothing to register. Each pool's
/// EstimatedBytes is O(1) (a handful of multiplications against its own backing arrays'
/// .Length), not O(entity count), so a sample stays cheap regardless of world size -- but
/// enumerating every registered pool is still heavier than a single Stopwatch bracket, so Tick
/// only re-samples once its sample interval has elapsed since the last sample, not every frame.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentMemoryTracker(ComponentManager componentManager)
{
    private static readonly long SampleIntervalTimestampTicks = System.Diagnostics.Stopwatch.Frequency * 2;

    private readonly List<ComponentMemoryEntry> _snapshot = [];

    private long _lastSampleTimestamp;
    private bool _hasSampled;

    /// <summary>Every registered component type's last-sampled Count/EstimatedBytes, descending by bytes.</summary>
    /// <remarks>Empty until the first sample.</remarks>
    public IReadOnlyList<ComponentMemoryEntry> Snapshot => _snapshot;

    /// <summary>Re-samples every registered pool if its sample interval has elapsed since the last sample.</summary>
    public void Tick()
    {
        var nowTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_hasSampled && nowTimestamp - _lastSampleTimestamp < SampleIntervalTimestampTicks)
        {
            return;
        }

        _snapshot.Clear();
        foreach (var pool in componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                _snapshot.Add(new ComponentMemoryEntry(pool.ComponentType.Name, memoryReportingPool.Count, memoryReportingPool.EstimatedBytes));
            }
        }

        _snapshot.Sort(static (a, b) => b.EstimatedBytes.CompareTo(a.EstimatedBytes));
        _hasSampled = true;
        _lastSampleTimestamp = nowTimestamp;
    }
}
