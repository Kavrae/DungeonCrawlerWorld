namespace Engine.ECS.Components;

public interface IInspectableComponentPool : IComponentPool
{
    int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination);
}