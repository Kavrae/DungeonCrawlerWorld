using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;

namespace Game.Modules.Core;

/// <summary>Shared read helper for NonBlockingComponent's MultiComponentPool.</summary>
/// <remarks>
/// MultiComponentPool has no built-in way to combine every instance an entity owns into one
/// answer -- an entity may hold several NonBlockingComponent sources at once (e.g. overlapping
/// Tiny and Phasing grants), and the pool only exposes a generic dense-chain walk
/// (GetFirstDenseIndex/GetNextDenseIndex), deliberately blind to what NonBlockingKind values
/// mean. This class owns the OR-combine rule in one place (same shape as
/// ActionInstanceQueries/ActionHotkeyBindingQueries) instead of every caller re-walking the
/// chain itself.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class NonBlockingQueries
{
    /// <summary>Combines the kinds of all active non-blocking components for the specified entity.</summary>
    /// <remarks>Combines multiple non-blocking component types into a single NonBlockingKind flag.</remarks>
    /// <param name="nonBlockingComponents">The multi-component pool containing non-blocking components.</param>
    /// <param name="entityId">The ID of the entity for which to combine component kinds.</param>
    /// <returns>The combined non-blocking kind.</returns>
    public static NonBlockingKind CombinedKind(MultiComponentPool<NonBlockingComponent> nonBlockingComponents, int entityId)
    {
        var combined = NonBlockingKind.None;
        for (var denseIndex = nonBlockingComponents.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = nonBlockingComponents.GetNextDenseIndex(denseIndex))
        {
            combined |= nonBlockingComponents.GetReadonlyByDenseIndex(denseIndex).Kind;
        }

        return combined;
    }
}
