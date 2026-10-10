namespace Engine.ECS.Relationships;

/// <summary>The rules of one relationship type, declared when it is registered.</summary>
/// <param name="OnTargetDestroyed">What happens to a target's sources when it is destroyed.</param>
/// <param name="Acyclic">Whether a link that would close a cycle is refused, for a hierarchy (a building's parts).</param>
/// <param name="MaximumDepth">For an acyclic relationship, the longest chain of links a new one may complete; past it the link is refused.</param>
public sealed record RelationshipSpec(TargetDestroyedPolicy OnTargetDestroyed, bool Acyclic = false, int MaximumDepth = 8);
