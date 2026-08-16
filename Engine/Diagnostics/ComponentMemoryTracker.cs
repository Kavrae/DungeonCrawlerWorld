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
/// only re-samples once SampleInterval has elapsed since the last sample, not every frame.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentMemoryTracker(ComponentManager componentManager)
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);

    private readonly ComponentManager _componentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));
    private readonly List<ComponentMemoryEntry> _snapshot = [];

    private DateTime _lastSampleUtc = DateTime.MinValue;

    /// <summary>Every registered component type's last-sampled Count/EstimatedBytes, descending by bytes.</summary>
    /// <remarks>Empty until the first sample.</remarks>
    public IReadOnlyList<ComponentMemoryEntry> Snapshot => _snapshot;

    /// <summary>Re-samples every registered pool if SampleInterval has elapsed since the last sample.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSampleUtc < SampleInterval)
        {
            return;
        }

        _snapshot.Clear();
        foreach (var pool in _componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                _snapshot.Add(new ComponentMemoryEntry(pool.ComponentType.Name, memoryReportingPool.Count, memoryReportingPool.EstimatedBytes));
            }
        }

        _snapshot.Sort(static (a, b) => b.EstimatedBytes.CompareTo(a.EstimatedBytes));
        _lastSampleUtc = now;
    }
}
