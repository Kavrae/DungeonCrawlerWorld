using Engine.Collections;
using Engine.ECS.Components;
using static Engine.ECS.Components.EntityCapacityGrowth;

namespace Engine.ECS.Entities;

/// <summary>Manages the lifecycle of entities.</summary>
/// <remarks>Entity ids are recycled via <see cref="FreeIdPool"/>.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityManager
{
    private readonly ComponentManager _componentManager;
    private readonly FreeIdPool _entityIdPool;
    private int _capacity;

    public EntityManager(ComponentManager componentManager, int initialCapacity)
    {
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _componentManager = componentManager;
        _capacity = initialCapacity;
        _entityIdPool = new FreeIdPool(initialCapacity);
    }

    public int Capacity => _capacity;

    /// <summary>Number of entities in the game.</summary>
    public int LivingEntityCount => _entityIdPool.Count;

    /// <summary>Creates a new entity id via pool rental.</summary>
    /// <returns>The id of the created entity.</returns>
    public int CreateEntity()
    {
        var entityId = _entityIdPool.Rent();

        if (entityId >= _capacity)
        {
            _capacity = NextCapacityFor(_capacity, entityId);
            _componentManager.ResizeEntityCapacity(_capacity);
        }

        return entityId;
    }

    /// <summary>Removes all components from the specified entity and then releases its id.</summary>
    /// <param name="entityId">The id of the entity to destroy.</param>
    public void DestroyEntity(int entityId)
    {
        _componentManager.RemoveAllComponents(entityId);
        _entityIdPool.Release(entityId);
    }

    /// <summary>Checks if the specified entity exists by id.</summary>
    /// <param name="entityId">The id of the entity to check.</param>
    /// <returns><c>true</c> if the entity exists; otherwise, <c>false</c>.</returns>
    public bool EntityExists(int entityId) => _entityIdPool.IsIssued(entityId);
}