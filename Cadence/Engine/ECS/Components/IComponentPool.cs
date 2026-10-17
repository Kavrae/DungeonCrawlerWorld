namespace Engine.ECS.Components;

/// <summary>Represents a pool for managing components of a specific type.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IComponentPool
{
    Type ComponentType { get; }

    /// <summary>Told about every by-entity read or write of this pool, in Debug builds only; null for none. See IEntityAccessGuard.</summary>
    IEntityAccessGuard? AccessGuard { get; set; }

    bool Has(int entityId);

    void Resize(int newMaximumEntityCount);

    /// <summary>Grows dense storage, if it is smaller, to hold at least minimumCount components without growing again. A no-op for a pool indexed by entity id, which Resize sizes.</summary>
    void ReserveDenseCapacity(int minimumCount);

    bool Remove(int entityId);
}