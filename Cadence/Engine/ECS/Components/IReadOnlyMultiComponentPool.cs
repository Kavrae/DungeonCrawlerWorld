namespace Engine.ECS.Components;

/// <summary>Provides read-only access to a multi-component pool.</summary>
/// <typeparam name="T">The type of the component.</typeparam>
/// <cleanupVersion>1</cleanupVersion>
public interface IReadOnlyMultiComponentPool<T> : IComponentPool where T : struct
{
    /// <summary>Gets the number of components of this type for the specified entity.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The number of components.</returns>
    int CountForEntity(int entityId);

    /// <summary>Gets the version of the components for this entity.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The version of the components.</returns>
    uint GetEntityVersion(int entityId);

    /// <summary>Gets the first dense index for the specified entity's components.</summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The first dense index.</returns>
    int GetFirstDenseIndex(int entityId);

    /// <summary>Gets the next dense index after the specified one.</summary>
    /// <param name="denseIndex">The current dense index.</param>
    /// <returns>The next dense index.</returns>
    int GetNextDenseIndex(int denseIndex);

    /// <summary>Gets a read-only reference to the component at the specified dense index.</summary>
    /// <param name="denseIndex">The dense index.</param>
    /// <returns>The component.</returns>
    ref readonly T GetReadonlyByDenseIndex(int denseIndex);

    /// <summary>Gets the entity ID for the component at the specified dense index.</summary>
    /// <param name="denseIndex">The dense index.</param>
    /// <returns>The entity ID.</returns>
    int GetEntityIdByDenseIndex(int denseIndex);

    /// <summary>Gets the version of the component at the specified dense index.</summary>
    /// <param name="denseIndex">The dense index.</param>
    /// <returns>The version of the component.</returns>
    uint GetVersionByDenseIndex(int denseIndex);
}