namespace Engine.ECS.Components;

public interface IComponentPool
{
    Type ComponentType { get; }
    ComponentPoolType ComponentPoolType { get; }

    bool Has(int entityId);

    void Resize(int newMaximumEntityCount);
    bool Remove(int entityId);
}
