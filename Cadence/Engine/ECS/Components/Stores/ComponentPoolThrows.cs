using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary>The pools' exceptions, built out of line so the by-entity accessors that throw them stay small enough to inline.</summary>
/// <cleanupVersion>1</cleanupVersion>
internal static class ComponentPoolThrows
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void MissingComponent<T>(int entityId) =>
        throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DuplicateComponent<T>(int entityId) =>
        throw new InvalidOperationException($"Entity {entityId} already has a component of type {typeof(T).Name}.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void StructuralChangeDuringWrite<T>() =>
        throw new InvalidOperationException($"The {typeof(T).Name} pool was added to, removed from or resized while a write scope on it was open: end the scope first.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Unobserved<T>() =>
        throw new InvalidOperationException($"The {typeof(T).Name} pool is not observed: register it with {nameof(ComponentChangeObservation)}.{nameof(ComponentChangeObservation.Observed)} to subscribe to its changes.");
}
