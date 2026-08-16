using Engine.ECS.Components.Stores;
using Engine.Math;

namespace Engine.ECS.Systems;

/// <summary>Shared chain-walk-and-splat mechanics for reacting to a MultiComponentPool&lt;TSource&gt;entity's own lifecycle</summary>
/// <remarks>
/// Deliberaly avoid specific implementation details of what "splat" actually does, so that this can be reused for different kinds of source splatting (e.g. light sources, sound sources, etc.)
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class SourceSplatting
{
    /// <summary>Splats every currently-registered source once, if it's on the map.</summary>
    public static void ScatterAll<TSource>(MultiComponentPool<TSource> sources, Func<int, Vector3Int?> tryGetPosition, Action<int, TSource, Vector3Int> splat)
        where TSource : struct
    {
        var entityIds = sources.EntityIds;
        var components = sources.Components;
        for (var i = 0; i < entityIds.Length; i++)
        {
            if (tryGetPosition(entityIds[i]) is { } position)
            {
                splat(entityIds[i], components[i], position);
            }
        }
    }

    /// <summary> Chain-walks every one of entityId's own source instances, unsplatting each oldPosition and splatting each newPosition </summary> 
    /// <remarks> The shared remove-old/add-new shape both a moved source and a first-time placement need, regardless of what "splat" actually accumulates. </remarks>
    public static void ResyncEntity<TSource>(MultiComponentPool<TSource> sources, int entityId, Vector3Int? oldPosition, Vector3Int newPosition, Action<TSource, Vector3Int> unsplat, Action<TSource, Vector3Int> splat)
        where TSource : struct
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var source = sources.GetReadonlyByDenseIndex(denseIndex);
            if (oldPosition is { } old)
            {
                unsplat(source, old);
            }

            splat(source, newPosition);
        }
    }
}
