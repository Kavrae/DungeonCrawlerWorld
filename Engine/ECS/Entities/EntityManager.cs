using Engine.Collections;
using Engine.ECS.Components;

namespace Engine.ECS.Entities;

/// <summary>
/// Owns entity id lifecycle. Ids are recycled via <see cref="FreeIdPool"/> so component
/// pool arrays stay bounded to the high-water mark of concurrently living entities rather
/// than growing forever across churn, and growing the id space transparently resizes every
/// registered component pool.
/// </summary>
public sealed class EntityManager
{
    private readonly ComponentManager _componentManager;
    private readonly FreeIdPool _freeIds;
    private int _capacity;

    public EntityManager(ComponentManager componentManager, int initialCapacity)
    {
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _componentManager = componentManager;
        _capacity = initialCapacity;
        _freeIds = new FreeIdPool(initialCapacity);
    }

    public int Capacity => _capacity;

    /// <summary>Number of entities currently alive (created and not yet destroyed).</summary>
    public int LivingEntityCount => _freeIds.Count;

    public int CreateEntity()
    {
        var entityId = _freeIds.Rent();

        if (entityId >= _capacity)
        {
            var newCapacity = _capacity * 2;
            if (newCapacity <= entityId)
            {
                newCapacity = entityId + 1;
            }

            _capacity = newCapacity;
            _componentManager.ResizeEntityCapacity(_capacity);
        }

        return entityId;
    }

    public void DestroyEntity(int entityId)
    {
        _componentManager.RemoveAllComponents(entityId);
        _freeIds.Release(entityId);
    }

    public bool IsAlive(int entityId) => _freeIds.IsIssued(entityId);
}