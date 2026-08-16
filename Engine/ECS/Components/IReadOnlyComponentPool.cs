namespace Engine.ECS.Components;

/// <summary> Provides read-only access to a component pool. </summary>
/// <typeparam name="T">The type of the component.</typeparam>
/// <cleanupVersion>1</cleanupVersion>
public interface IReadOnlyComponentPool<T> : IComponentPool where T : struct
{
    /// <summary>Attempts to retrieve a read-only reference to the component for the specified entity.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="component">The component, if found.</param>
    /// <returns>True if the component was found; otherwise, false.</returns>
    bool TryGetReadonly(int entityId, out T component);

    /// <summary>Retrieves a read-only reference to the component for the specified entity.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The component.</returns>
    ref readonly T GetReadonly(int entityId);

    /// <summary>Retrieves the version of the component for the specified entity.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The version of the component.</returns>
    uint GetVersion(int entityId);
}