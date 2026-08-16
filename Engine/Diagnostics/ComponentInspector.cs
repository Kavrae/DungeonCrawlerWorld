using Engine.ECS.Components;

namespace Engine.Diagnostics;

/// <summary>Enabled the debug inspection of component data</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentInspector(ComponentManager componentManager)
{
    private readonly ComponentManager _componentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));

    /// <summary>Copies the inspection data for all components of the specified entity.</summary>
    /// <param name="entityId">The ID of the entity for which to copy inspection data.</param>
    /// <param name="destination">The list to which to add the inspection data.</param>
    public void CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var pool in _componentManager.AllPools)
        {
            if (pool is IInspectableComponentPool inspectable)
            {
                inspectable.CopyInspectionDataForEntity(entityId, destination);
            }
        }
    }
}