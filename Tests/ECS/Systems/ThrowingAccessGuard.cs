using Engine.ECS.Components;

namespace Tests.ECS.Systems;

/// <summary>An access guard that refuses every entity: what a skeleton's pools look like to the simulation.</summary>
internal sealed class ThrowingAccessGuard : IEntityAccessGuard
{
    public void OnAccess(Type componentType, int entityId) =>
        throw new InvalidOperationException($"{componentType.Name} of entity {entityId} was read.");
}
