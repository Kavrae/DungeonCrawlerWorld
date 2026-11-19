using Engine.ECS.Properties;

namespace Engine.ECS.Relationships;

/// <summary>RelatedSourceComponent's table, by hand: it is generic, so no module initializer can name its closed types, and ComponentManager.RegisterRelationship registers one per link type.</summary>
/// <remarks>Its one row is the source's entity id: kept by the relationship, never written through a table, never saved (rebuilt from the links).</remarks>
internal sealed class RelatedSourceComponentPropertyAccess<TLink> : ComponentPropertyAccess<RelatedSourceComponent<TLink>>
    where TLink : struct, IRelationshipLink
{
    private static readonly ComponentPropertyDescriptor SourceEntityIdProperty = new(
        0,
        nameof(RelatedSourceComponent<TLink>.SourceEntityId),
        ComponentPropertyKind.Integer,
        typeof(int),
        new ComponentPropertyMeaning(ComponentPropertyMeaningNames.EntityId),
        ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.NotSaved);

    public static readonly RelatedSourceComponentPropertyAccess<TLink> Instance = new();

    public override ComponentPropertyTable Table { get; } = new(typeof(RelatedSourceComponent<TLink>), [SourceEntityIdProperty]);

    public override void Read(in RelatedSourceComponent<TLink> component, ComponentPropertyVisitor visitor) =>
        visitor.Integer(SourceEntityIdProperty, component.SourceEntityId);

    public override bool Write(ref RelatedSourceComponent<TLink> component, ReadOnlySpan<int> path, in ComponentPropertyValue value) => false;

    /// <remarks>Never restored: the relationship rebuilds a target's sources from the links restored onto them.</remarks>
    public override bool Restore(ref RelatedSourceComponent<TLink> component, ReadOnlySpan<int> path, in ComponentPropertyValue value) => false;
}
