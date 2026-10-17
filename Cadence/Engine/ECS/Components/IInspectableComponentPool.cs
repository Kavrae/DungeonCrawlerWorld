namespace Engine.ECS.Components;

/// <summary> Provides inspection capabilities for a component pool.  </summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IInspectableComponentPool : IComponentPool
{
    /// <summary>Copies inspection data from all components for a specific entity.</summary>
    /// <param name="entityId">The entity to retrieve data for</param>
    /// <param name="destination">The list to copy inspection data into</param>
    /// <returns>The number of inspection entries copied</returns>
    int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination);
}