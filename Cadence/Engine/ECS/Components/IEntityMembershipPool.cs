namespace Engine.ECS.Components;

/// <summary>Provides access to the set of entities that have components in the pool.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IEntityMembershipPool : IComponentPool
{
    /// <summary>Gets the IDs of all entities that have components in the pool.</summary>
    ReadOnlySpan<int> EntityIds { get; }

    /// <summary>Occurs when an entity is added to the pool.</summary>
    event Action<int>? EntityAdded;

    /// <summary>Occurs when an entity is removed from the pool.</summary>
    event Action<int>? EntityRemoved;
}
