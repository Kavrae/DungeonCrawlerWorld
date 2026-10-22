namespace Game.Areas;

/// <summary>Which unique blueprints have one living in the session, and which have been killed in it: one of each lives at a time, and one killed never comes back this session.</summary>
/// <remarks>
/// Keyed by the blueprint's Guid. A unique placement asks IsAvailable when it is about to spawn -- on the main thread,
/// so two neighborhoods planned at once can never both place it -- and reports what it spawned with Placed. The build
/// reports deaths (EntityDiedEvent) and destructions (EntityManager.EntityDestroying): a unique destroyed without dying,
/// by its neighborhood unloading, is available again. Saved with the save system later.
/// </remarks>
public sealed class UniqueEntityRoster
{
    private readonly Dictionary<int, Guid> _uniqueBlueprintByLivingEntity = [];
    private readonly HashSet<Guid> _livingUniqueBlueprints = [];
    private readonly HashSet<Guid> _killedUniqueBlueprints = [];

    /// <summary>Whether blueprint may be spawned as a unique now: none of it is alive and none was killed this session.</summary>
    public bool IsAvailable(Guid blueprint) => !_livingUniqueBlueprints.Contains(blueprint) && !_killedUniqueBlueprints.Contains(blueprint);

    public bool WasKilled(Guid blueprint) => _killedUniqueBlueprints.Contains(blueprint);

    /// <summary>entityId is now the living unique of blueprint.</summary>
    /// <exception cref="InvalidOperationException">blueprint isn't available.</exception>
    public void Placed(Guid blueprint, int entityId)
    {
        if (!IsAvailable(blueprint))
        {
            throw new InvalidOperationException($"Unique blueprint {blueprint} isn't available: one is alive or was killed this session.");
        }

        _livingUniqueBlueprints.Add(blueprint);
        _uniqueBlueprintByLivingEntity[entityId] = blueprint;
    }

    /// <summary>entityId died: if it was a unique, its blueprint never comes back this session.</summary>
    public void EntityDied(int entityId)
    {
        if (_uniqueBlueprintByLivingEntity.TryGetValue(entityId, out var blueprint))
        {
            _killedUniqueBlueprints.Add(blueprint);
        }
    }

    /// <summary>entityId is being destroyed: if it was a unique, it no longer lives.</summary>
    public void EntityDestroying(int entityId)
    {
        if (_uniqueBlueprintByLivingEntity.Remove(entityId, out var blueprint))
        {
            _livingUniqueBlueprints.Remove(blueprint);
        }
    }
}
