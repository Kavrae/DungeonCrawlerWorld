namespace Engine.ECS.Components;

/// <summary>Told about every by-entity read or write of a pool it guards, in Debug builds only.</summary>
/// <remarks>
/// For invariants of the form "nothing may touch this pool for that entity yet", which no single
/// caller can check for the others. Pools call it through a [Conditional("DEBUG")] method, so a
/// Release build pays nothing and never calls it. Removal is not reported: destroying an entity
/// must always be allowed.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IEntityAccessGuard
{
    void OnAccess(Type componentType, int entityId);
}
