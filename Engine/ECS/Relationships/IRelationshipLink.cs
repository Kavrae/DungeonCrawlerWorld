using Engine.ECS.Entities;

namespace Engine.ECS.Relationships;

/// <summary>The source side of a relationship: a component naming the one entity its holder is linked to.</summary>
/// <remarks>
/// The link is the relationship's source of truth and the only side code writes, through its pool like any
/// component. Its Relationship keeps the target's side (RelatedSourceComponent) in step with every write.
/// The target is held by key, not id, because a link outlives the frame; it is what a save persists.
/// </remarks>
public interface IRelationshipLink
{
    EntityKey TargetKey { get; }
}
