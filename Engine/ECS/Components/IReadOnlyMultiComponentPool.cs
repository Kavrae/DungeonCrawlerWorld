namespace Engine.ECS.Components;

public interface IReadOnlyMultiComponentPool<T> : IComponentPool where T : struct
{
    int CountForEntity(int entityId);
    uint GetEntityVersion(int entityId);

    int GetFirstDenseIndex(int entityId);
    int GetNextDenseIndex(int denseIndex);

    ref readonly T GetReadonlyByDenseIndex(int denseIndex);
    int GetEntityIdByDenseIndex(int denseIndex);
    uint GetVersionByDenseIndex(int denseIndex);
}
