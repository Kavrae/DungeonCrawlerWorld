namespace Engine.ECS.Relationships;

/// <summary>The target side of a relationship: one entity linked to the holder by a TLink.</summary>
/// <remarks>
/// One instance per source, in a Multi pool written only by the relationship itself: ComponentManager refuses
/// to hand the pool out for writing. Holds the source's id rather than its key because the relationship removes
/// the instance before the source's id is released. Never saved; it rebuilds from the links.
/// </remarks>
public readonly struct RelatedSourceComponent<TLink> where TLink : struct, IRelationshipLink
{
    internal RelatedSourceComponent(int sourceEntityId) => SourceEntityId = sourceEntityId;

    public int SourceEntityId { get; }

    public override string ToString() => $"{typeof(TLink).Name} from : {SourceEntityId}";
}
