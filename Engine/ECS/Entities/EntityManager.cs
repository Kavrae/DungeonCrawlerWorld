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

    /// <param name="keys">The key table to issue into, when something needs it before this manager exists; a new one otherwise.</param>
    public EntityManager(ComponentManager componentManager, int initialCapacity, EntityKeys? keys = null)
    {
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _componentManager = componentManager;
        _capacity = initialCapacity;
        _entityIdPool = new FreeIdPool(initialCapacity);
        Keys = keys ?? new EntityKeys();
    }

    public int Capacity => _capacity;

    /// <summary>Every living entity's stable key -- see EntityKeys.</summary>
    public EntityKeys Keys { get; }

    /// <summary>Raised by DestroyEntity before the entity's components, id and key are released, so a handler can still read them.</summary>
    /// <remarks>Where anything holding state outside the component pools -- a map index, a grid, a UI selection -- lets go of a destroyed entity, whatever destroyed it.</remarks>
    public event Action<int>? EntityDestroying;

    /// <summary>Number of entities in the game.</summary>
    public int LivingEntityCount => _entityIdPool.Count;

    /// <summary>Creates a new entity id via pool rental.</summary>
    /// <returns>The id of the created entity.</returns>
    public int CreateEntity()
    {
        var entityId = _entityIdPool.Rent();
        Keys.Issue(entityId);

        if (entityId >= _capacity)
        {
            _capacity = NextCapacityFor(_capacity, entityId);
            _componentManager.ResizeEntityCapacity(_capacity);
        }

        return entityId;
    }

    /// <summary>Grows entity capacity, where smaller, so ids below capacity never trigger a reallocation of every entity-indexed pool.</summary>
    public void ReserveCapacity(int capacity)
    {
        _entityIdPool.Reserve(capacity);
        Keys.Reserve(capacity);
        if (capacity > _capacity)
        {
            _capacity = capacity;
            _componentManager.ResizeEntityCapacity(capacity);
        }
    }

    /// <summary>Raises EntityDestroying, removes all components from the specified entity, then releases its id and its key.</summary>
    /// <param name="entityId">The id of the entity to destroy.</param>
    public void DestroyEntity(int entityId)
    {
        EntityDestroying?.Invoke(entityId);
        _componentManager.RemoveAllComponents(entityId);
        Keys.Release(entityId);
        _entityIdPool.Release(entityId);
    }

    /// <summary>Checks if the specified entity exists by id.</summary>
    /// <param name="entityId">The id of the entity to check.</param>
    /// <returns><c>true</c> if the entity exists; otherwise, <c>false</c>.</returns>
    public bool EntityExists(int entityId) => _entityIdPool.IsIssued(entityId);
}