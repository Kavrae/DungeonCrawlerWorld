namespace Engine.ECS.Components;

/// <summary>Component pool capability for estimating a pool's own memory footprint.</summary>
/// <remarks>
/// Implemented by every concrete pool store (Direct/Packed/Multi), so a new component type gets
/// memory reporting automatically -- see ComponentMemoryTracker, which enumerates
/// ComponentManager.AllPools filtering for this the same way ComponentInspector filters for
/// IInspectableComponentPool.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IMemoryReportingComponentPool : IComponentPool
{
    /// <summary>The number of components currently stored in the pool.</summary>
    int Count { get; }

    /// <summary>Estimated total bytes occupied by this pool's backing storage -- every internal array, not just the component values themselves.</summary>
    long EstimatedBytes { get; }
}
