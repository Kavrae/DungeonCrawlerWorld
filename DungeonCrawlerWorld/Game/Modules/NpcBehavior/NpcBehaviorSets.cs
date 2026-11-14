using Engine.Decisions;
using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Spawning;

namespace Game.Modules.NpcBehavior;

/// <summary>The behavior set every blueprint's entities decide with, resolved once per blueprint and kept by its id.</summary>
/// <remarks>
/// A set's id is the id of the blueprint it is resolved from, so an entity stores only that (NpcDecisionComponent). An
/// entity with parts applied to it decides with the set of its spawn blueprint and those parts in order, composed into
/// a blueprint of their own (BlueprintRegistry.Compose), so a Boss applied mid-fight resolves once per distinct
/// combination, never per decision. Resolving needs the catalog frozen: ResolveAll, at the end of NpcBehaviorModule's
/// RegisterBehavior, resolves every registered blueprint, so a grant naming no behavior fails the build -- or a mod's dry
/// run -- rather than a spawn.
/// </remarks>
public sealed class NpcBehaviorSets(BlueprintRegistry definitions, BehaviorCatalog<NpcDecisionContext> catalog)
{
    private BehaviorSet<NpcDecisionContext>?[] _setsByBlueprintId = [];

    /// <summary>Resolves every registered blueprint's set now.</summary>
    /// <exception cref="InvalidOperationException">A blueprint grants a behavior no module registered, or names a modifier tag outside the Behavior domain.</exception>
    public void ResolveAll()
    {
        for (var blueprintId = (ushort)1; blueprintId <= definitions.Count; blueprintId++)
        {
            try
            {
                Get(blueprintId);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"Blueprint '{definitions.Get(blueprintId).Name}': {exception.Message}", exception);
            }
        }
    }

    /// <summary>The set entities of blueprintId decide with; empty when none of its parts grants a behavior.</summary>
    public BehaviorSet<NpcDecisionContext> Get(ushort blueprintId)
    {
        if (blueprintId < _setsByBlueprintId.Length && _setsByBlueprintId[blueprintId] is { } cached)
        {
            return cached;
        }

        var resolved = definitions.Resolve(blueprintId);
        var set = resolved.BehaviorGrantLists.Count == 0 && resolved.BehaviorModifiers.Count == 0
            ? BehaviorSet<NpcDecisionContext>.Empty
            : BehaviorSet<NpcDecisionContext>.Resolve(catalog, resolved.BehaviorGrantLists, resolved.BehaviorModifiers);

        if (blueprintId >= _setsByBlueprintId.Length)
        {
            Array.Resize(ref _setsByBlueprintId, System.Math.Max(blueprintId + 1, definitions.Count + 1));
        }

        _setsByBlueprintId[blueprintId] = set;
        return set;
    }

    /// <summary>The id of the set entityId decides with: its spawn blueprint's, or the composition of it and every part applied to it, in the order they were applied.</summary>
    public ushort SetIdFor(int entityId, DirectComponentPool<SpawnRecordComponent> spawnRecords, MultiComponentPool<AppliedBlueprintComponent> appliedParts)
    {
        var spawnBlueprintId = spawnRecords.GetReadonly(entityId).BlueprintId;
        if (!appliedParts.Has(entityId))
        {
            return spawnBlueprintId;
        }

        var applied = new List<AppliedBlueprintComponent>();
        for (var denseIndex = appliedParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = appliedParts.GetNextDenseIndex(denseIndex))
        {
            applied.Add(appliedParts.GetReadonlyByDenseIndex(denseIndex));
        }

        applied.Sort(static (first, second) => first.Order.CompareTo(second.Order));
        Guid[] includes = [definitions.Get(spawnBlueprintId).Id, .. applied.Select(part => definitions.Get(part.BlueprintId).Id)];
        return definitions.Compose(includes);
    }
}
