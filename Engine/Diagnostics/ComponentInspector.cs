using Engine.ECS.Components;

namespace Engine.Diagnostics;

/// <summary>
/// Structured entity inspection, wrapping every registered pool's
/// <see cref="IInspectableComponentPool"/>. Reads data the pools already track for their own
/// bookkeeping rather than reflecting over component fields at inspection time, so dumping
/// an entity's components (e.g. for a debug/selection UI) costs no reflection and no boxing
/// beyond the pools' own inspection entries.
/// </summary>
public sealed class ComponentInspector(ComponentManager componentManager)
{
    private readonly ComponentManager _componentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));

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