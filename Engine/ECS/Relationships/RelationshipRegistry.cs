using Engine.ECS.Entities;

namespace Engine.ECS.Relationships;

/// <summary>Every relationship type registered on a ComponentManager, and the component types that make them up.</summary>
/// <remarks>
/// EntityManager.DestroyEntity drives it: OnEntityDestroying first thing, destroying the sources it hands back
/// itself, then OnEntityRemovingComponents just before the entity's components go. Throughout, nothing can be
/// linked to the entity (ComponentManager.DestroyingEntityIds). The registry never destroys or creates anything,
/// so it holds no reference to the EntityManager.
/// </remarks>
public sealed class RelationshipRegistry
{
    private readonly List<IRelationship> _relationships = [];
    private readonly HashSet<Type> _componentTypes = [];
    private readonly HashSet<Type> _relatedSourceComponentTypes = [];

    /// <summary>Whether componentType is either side of a relationship: a link or a RelatedSourceComponent.</summary>
    /// <remarks>Either side may sit on any entity, built or not, so tooling that sorts pools by who can hold them asks this.</remarks>
    public bool IsRelationshipComponentType(Type componentType) => _componentTypes.Contains(componentType);

    /// <summary>Whether componentType is a relationship's target side, which only the relationship writes.</summary>
    public bool IsRelatedSourceComponentType(Type componentType) => _relatedSourceComponentTypes.Contains(componentType);

    internal void Add(IRelationship relationship, Type linkComponentType, Type relatedSourceComponentType)
    {
        _relationships.Add(relationship);
        _componentTypes.Add(linkComponentType);
        _componentTypes.Add(relatedSourceComponentType);
        _relatedSourceComponentTypes.Add(relatedSourceComponentType);
    }

    /// <summary>Applies each relationship's TargetDestroyedPolicy to the sources linked to entityId, adding those to destroy to sourcesToDestroy.</summary>
    /// <remarks>Sources already being destroyed are only detached.</remarks>
    internal void OnEntityDestroying(int entityId, List<EntityKey> sourcesToDestroy)
    {
        for (var index = 0; index < _relationships.Count; index++)
        {
            _relationships[index].OnTargetDestroying(entityId, sourcesToDestroy);
        }
    }

    /// <summary>Detaches entityId's own links as SourceDestroyed, before its components are removed.</summary>
    internal void OnEntityRemovingComponents(int entityId)
    {
        for (var index = 0; index < _relationships.Count; index++)
        {
            _relationships[index].OnSourceDestroying(entityId);
        }
    }
}
