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

    /// <summary>Lists of sources a relationship hands back to destroy, one per DestroyEntity in progress, reused across calls.</summary>
    private readonly Stack<List<EntityKey>> _sourcesToDestroyListPool = new();

    public EntityManager(ComponentManager componentManager, int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _componentManager = componentManager;
        _capacity = initialCapacity;
        _entityIdPool = new FreeIdPool(initialCapacity);
    }

    public int Capacity => _capacity;

    /// <summary>Every living entity's stable key -- see EntityKeys.</summary>
    /// <remarks>The ComponentManager's table, so relationships resolve links through the same keys this issues.</remarks>
    public EntityKeys Keys => _componentManager.Keys;

    /// <summary>The entities DestroyEntity is currently running for; more than one only when destroying one entity destroys another.</summary>
    /// <remarks>The ComponentManager's set, so relationships refuse the same entities as targets.</remarks>
    private HashSet<int> DestroyingEntityIds => _componentManager.DestroyingEntityIds;

    /// <summary>How many entities DestroyEntity has destroyed, counting each one a destroy takes with it.</summary>
    public long TotalEntitiesDestroyed { get; private set; }

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

    /// <summary>Applies relationship cleanup, raises EntityDestroying, removes all components from the specified entity, then releases its id and its key.</summary>
    /// <remarks>
    /// Relationship cleanup comes first, so a relationship that destroys its sources has them gone before the
    /// entity's own EntityDestroying handlers run; the entity's own links are detached after those handlers,
    /// just before its components go, so it is still linked while they run (see RelationshipRegistry).
    /// </remarks>
    /// <param name="entityId">The id of the entity to destroy.</param>
    public void DestroyEntity(int entityId)
    {
        DestroyingEntityIds.Add(entityId);
        try
        {
            DestroyLinkedSources(entityId);
            EntityDestroying?.Invoke(entityId);
            _componentManager.Relationships.OnEntityRemovingComponents(entityId);
            _componentManager.RemoveAllComponents(entityId);
        }
        finally
        {
            DestroyingEntityIds.Remove(entityId);
        }

        Keys.Release(entityId);
        _entityIdPool.Release(entityId);
        TotalEntitiesDestroyed++;
    }

    /// <summary>Applies every relationship's TargetDestroyedPolicy to entityId's sources, destroying the ones it hands back.</summary>
    /// <remarks>Sources come back as keys: destroying one can destroy another on the list first, and its id may be reused by then.</remarks>
    private void DestroyLinkedSources(int entityId)
    {
        var sourcesToDestroy = _sourcesToDestroyListPool.TryPop(out var pooledList) ? pooledList : [];
        _componentManager.Relationships.OnEntityDestroying(entityId, sourcesToDestroy);

        foreach (var sourceKey in sourcesToDestroy)
        {
            if (Keys.TryGetEntityId(sourceKey, out var sourceEntityId) && !DestroyingEntityIds.Contains(sourceEntityId))
            {
                DestroyEntity(sourceEntityId);
            }
        }

        sourcesToDestroy.Clear();
        _sourcesToDestroyListPool.Push(sourcesToDestroy);
    }

    /// <summary>Whether the specified entity is inside DestroyEntity: from before EntityDestroying is raised until its components are removed.</summary>
    /// <remarks>For anything that reacts to a component being removed and must tell a removal that is part of destroying the entity from one that isn't.</remarks>
    public bool IsDestroying(int entityId) => DestroyingEntityIds.Contains(entityId);

    /// <summary>Checks if the specified entity exists by id.</summary>
    /// <param name="entityId">The id of the entity to check.</param>
    /// <returns><c>true</c> if the entity exists; otherwise, <c>false</c>.</returns>
    public bool EntityExists(int entityId) => _entityIdPool.IsIssued(entityId);
}