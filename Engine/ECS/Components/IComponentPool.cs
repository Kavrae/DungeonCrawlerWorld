namespace Engine.ECS.Components;

/// <summary>Represents a pool for managing components of a specific type.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IComponentPool
{
    Type ComponentType { get; }

    bool Has(int entityId);

    void Resize(int newMaximumEntityCount);
    bool Remove(int entityId);
}