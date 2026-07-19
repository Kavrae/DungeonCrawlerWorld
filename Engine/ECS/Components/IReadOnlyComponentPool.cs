namespace Engine.ECS.Components;

public interface IReadOnlyComponentPool<T> : IComponentPool where T : struct
{
    bool TryGetReadonly(int entityId, out T component);
    ref readonly T GetReadonly(int entityId);
    uint GetVersion(int entityId);
}
