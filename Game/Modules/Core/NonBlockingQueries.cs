using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;

namespace Game.Modules.Core;

/// <summary>Shared read helper for NonBlockingComponent's MultiComponentPool -- same shape as ActionInstanceQueries/ActionHotkeyBindingQueries, walking the dense per-entity chain.</summary>
public static class NonBlockingQueries
{
    /// <summary>Every active source's Kind, OR-combined -- an entity with two overlapping sources (one Tiny, one Phasing) renders as both, the same "any active source" inclusive-OR philosophy IsBlocking itself already uses for the exemption fact.</summary>
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
