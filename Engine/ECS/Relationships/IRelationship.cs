using Engine.ECS.Entities;

namespace Engine.ECS.Relationships;

/// <summary>What RelationshipRegistry needs of every relationship type, whatever its link.</summary>
internal interface IRelationship
{
    /// <summary>Detaches every source linked to entityId and applies the TargetDestroyedPolicy: unlinks them, or adds them to sourcesToDestroy.</summary>
    void OnTargetDestroying(int entityId, List<EntityKey> sourcesToDestroy);

    /// <summary>Detaches entityId's own link, if it has one, as SourceDestroyed.</summary>
    void OnSourceDestroying(int entityId);
}
