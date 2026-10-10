namespace Engine.ECS.Relationships;

/// <summary>Handles a source losing its link to a target, with the link still readable.</summary>
/// <remarks>
/// For Retargeted, link is the value just written, naming the new target. link is a copy, unaffected by
/// anything the handler does. A handler may write other pools and destroy other entities -- other sources of
/// the same target included -- but must not write this relationship's links itself or destroy sourceEntityId.
/// </remarks>
public delegate void SourceUnlinkedHandler<TLink>(int sourceEntityId, int targetEntityId, in TLink link, UnlinkReason reason) where TLink : struct, IRelationshipLink;
