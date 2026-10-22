using Game.Areas;
using Game.Bootstrap;
using Game.World;

namespace Builders.MapAreas;

/// <summary>The Map tab's preview builds: each area's by area and seed, kept for the IDE's run, and the most recent neighborhood builds, which are large and built on a worker.</summary>
/// <remarks>
/// The session they are built from never changes, so a build never goes stale; a neighborhood build holds a whole
/// neighborhood's population, so only NeighborhoodBuildsKept are kept. Every member is called on the UI thread: a
/// neighborhood is built on a worker (a build shares nothing with another, so it may run beside the UI), and whoever
/// waits on it hands the finished build back with KeepNeighborhood.
/// </remarks>
/// <param name="buildSession">Builds a game session over a map with the IDE session's content.</param>
public sealed class AreaPreviewCache(Func<Map, GameSession> buildSession)
{
    public const int NeighborhoodBuildsKept = 2;

    private readonly Dictionary<(Guid AreaId, ulong Seed), AreaPreviewBuild> _builds = [];
    private readonly List<(NeighborhoodPreviewKey Key, NeighborhoodPreviewBuild Build)> _neighborhoodBuilds = [];
    private readonly Dictionary<NeighborhoodPreviewKey, Task<NeighborhoodPreviewBuild>> _neighborhoodBuildsRunning = [];

    public AreaPreviewBuild Get(AreaDefinition area, ulong seed)
    {
        if (!_builds.TryGetValue((area.Id, seed), out var build))
        {
            build = AreaPreviewBuild.Build(area, seed, buildSession);
            _builds[(area.Id, seed)] = build;
        }

        return build;
    }

    /// <summary>The neighborhood build for key if one is kept, made the most recently used.</summary>
    public bool TryGetNeighborhood(NeighborhoodPreviewKey key, out NeighborhoodPreviewBuild build)
    {
        var index = _neighborhoodBuilds.FindIndex(entry => entry.Key == key);
        if (index < 0)
        {
            build = null!;
            return false;
        }

        var kept = _neighborhoodBuilds[index];
        _neighborhoodBuilds.RemoveAt(index);
        _neighborhoodBuilds.Add(kept);
        build = kept.Build;
        return true;
    }

    /// <summary>Builds key's neighborhood on a worker, or hands back the build already running for it.</summary>
    public Task<NeighborhoodPreviewBuild> BuildNeighborhoodAsync(NeighborhoodPreviewKey key)
    {
        if (!_neighborhoodBuildsRunning.TryGetValue(key, out var running))
        {
            running = Task.Run(() => NeighborhoodPreviewBuild.Build(key.WorldSeed, key.CellX, key.CellY, buildSession));
            _neighborhoodBuildsRunning[key] = running;
        }

        return running;
    }

    /// <summary>A finished worker build: kept as the most recently used, the oldest dropped past NeighborhoodBuildsKept.</summary>
    public void KeepNeighborhood(NeighborhoodPreviewKey key, NeighborhoodPreviewBuild build)
    {
        _neighborhoodBuildsRunning.Remove(key);
        if (_neighborhoodBuilds.Any(entry => entry.Key == key))
        {
            return;
        }

        _neighborhoodBuilds.Add((key, build));
        if (_neighborhoodBuilds.Count > NeighborhoodBuildsKept)
        {
            _neighborhoodBuilds.RemoveAt(0);
        }
    }

    /// <summary>A worker build that failed: the next ask builds again.</summary>
    public void ForgetFailedNeighborhood(NeighborhoodPreviewKey key) => _neighborhoodBuildsRunning.Remove(key);
}

/// <summary>Which neighborhood preview: the world seed and the starting-window cell.</summary>
public readonly record struct NeighborhoodPreviewKey(int WorldSeed, int CellX, int CellY);
