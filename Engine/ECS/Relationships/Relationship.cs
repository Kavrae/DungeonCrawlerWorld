using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;

namespace Engine.ECS.Relationships;

/// <summary>One relationship type: links from sources to one target each, and every target's sources, kept in step.</summary>
/// <remarks>
/// <para>
/// The links (TLink, a Packed pool) are written like any component -- Add, Merge, TrySet, Remove, a blueprint
/// build step. This observes that pool (ComponentChanged, ComponentRemoving), so every write path keeps the
/// target side (RelatedSourceComponent, a Multi pool) right and no caller has to remember. Each source has at
/// most one target; writing a link naming another target moves it (Retargeted).
/// </para>
/// <para>
/// A link must name a living entity, not being destroyed, other than its source, and in an acyclic relationship
/// must not close a cycle; anything else throws. Link checks before writing, so its refusal leaves nothing behind;
/// a write straight to the pool is checked by the observer after the value is already stored, so its refusal
/// removes the link -- the source is left unlinked -- before throwing.
/// </para>
/// <para>
/// Destroying an entity (EntityManager.DestroyEntity, before EntityDestroying) applies the spec's
/// TargetDestroyedPolicy to its sources, handing back the ones to destroy for EntityManager to destroy. Its
/// own link is detached as SourceDestroyed just before its components go.
/// </para>
/// </remarks>
public sealed class Relationship<TLink> : IRelationship where TLink : struct, IRelationshipLink
{
    private readonly PackedComponentPool<TLink> _links;
    private readonly MultiComponentPool<RelatedSourceComponent<TLink>> _relatedSources;
    private readonly EntityKeys _entityKeys;
    private readonly IReadOnlySet<int> _destroyingEntityIds;
    private readonly EntityPages<int> _targetEntityIdBySourceEntityId;
    private readonly Stack<List<int>> _sourceEntityIdListPool = new();
    private int _sourceEntityIdBeingLinked = -1;
    private int _targetEntityIdBeingLinked = -1;

    internal Relationship(RelationshipSpec spec, PackedComponentPool<TLink> links, MultiComponentPool<RelatedSourceComponent<TLink>> relatedSources, EntityKeys entityKeys, IReadOnlySet<int> destroyingEntityIds, int entityCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.MaximumDepth);

        Spec = spec;
        _links = links;
        _relatedSources = relatedSources;
        _entityKeys = entityKeys;
        _destroyingEntityIds = destroyingEntityIds;
        _targetEntityIdBySourceEntityId = new EntityPages<int>(entityCapacity, empty: -1);

        links.ComponentChanged += OnLinkWritten;
        links.ComponentRemoving += OnLinkRemoving;
    }

    public RelationshipSpec Spec { get; }

    /// <summary>Every source's link, written like any Packed pool.</summary>
    public PackedComponentPool<TLink> Links => _links;

    /// <summary>Raised whenever a source stops being linked to a target -- see UnlinkReason.</summary>
    public event SourceUnlinkedHandler<TLink>? SourceUnlinked;

    /// <summary>Links sourceEntityId by link, replacing any link it had, after checking the link is allowed.</summary>
    public void Link(int sourceEntityId, TLink link)
    {
        _targetEntityIdBeingLinked = ResolveTargetOrThrow(sourceEntityId, link.TargetKey);
        _sourceEntityIdBeingLinked = sourceEntityId;
        try
        {
            _links.Merge(sourceEntityId, link);
        }
        finally
        {
            _sourceEntityIdBeingLinked = -1;
            _targetEntityIdBeingLinked = -1;
        }
    }

    /// <summary>Removes sourceEntityId's link, if it has one.</summary>
    public bool Unlink(int sourceEntityId) => _links.Remove(sourceEntityId);

    /// <summary>The entity sourceEntityId is linked to, if it is linked.</summary>
    public bool TryGetTargetEntityId(int sourceEntityId, out int targetEntityId)
    {
        targetEntityId = _targetEntityIdBySourceEntityId.Get(sourceEntityId);
        return targetEntityId >= 0;
    }

    /// <summary>How many sources are linked to targetEntityId.</summary>
    public int CountSources(int targetEntityId) => _relatedSources.CountForEntity(targetEntityId);

    /// <summary>Fills destination with every source linked to targetEntityId, clearing it first.</summary>
    public void CopySourceEntityIds(int targetEntityId, List<int> destination)
    {
        destination.Clear();
        AppendSourceEntityIds(targetEntityId, destination);
    }

    /// <summary>The top of entityId's chain of links: the entity it is linked to, followed until one isn't linked; entityId itself when it isn't linked.</summary>
    /// <remarks>Only for an acyclic relationship, whose chains end and are at most MaximumDepth links long; any other throws.</remarks>
    public int RootOf(int entityId)
    {
        ThrowIfNotAcyclic();

        var rootEntityId = entityId;
        while (_targetEntityIdBySourceEntityId.Get(rootEntityId) is var targetEntityId and >= 0)
        {
            rootEntityId = targetEntityId;
        }

        return rootEntityId;
    }

    /// <summary>Fills destination with every entity linked to entityId directly or through others, clearing it first; each comes after the one it is linked to.</summary>
    /// <remarks>Breadth-first, using destination as its own work list, so it allocates nothing once destination is large enough. Only for an acyclic relationship; any other throws.</remarks>
    public void CopyDescendantEntityIds(int entityId, List<int> destination)
    {
        ThrowIfNotAcyclic();

        destination.Clear();
        AppendSourceEntityIds(entityId, destination);
        for (var index = 0; index < destination.Count; index++)
        {
            AppendSourceEntityIds(destination[index], destination);
        }
    }

    private void AppendSourceEntityIds(int targetEntityId, List<int> destination)
    {
        for (var denseIndex = _relatedSources.GetFirstDenseIndex(targetEntityId); denseIndex != -1; denseIndex = _relatedSources.GetNextDenseIndex(denseIndex))
        {
            destination.Add(_relatedSources.GetReadonlyByDenseIndex(denseIndex).SourceEntityId);
        }
    }

    void IRelationship.OnTargetDestroying(int entityId, List<EntityKey> sourcesToDestroy)
    {
        if (_links.Count == 0 || !_relatedSources.Has(entityId))
        {
            return;
        }

        var sourceEntityIds = RentSourceEntityIdList();
        CopySourceEntityIds(entityId, sourceEntityIds);

        foreach (var sourceEntityId in sourceEntityIds)
        {
            if (_targetEntityIdBySourceEntityId.Get(sourceEntityId) != entityId)
            {
                continue;
            }

            var sourceIsDestroying = _destroyingEntityIds.Contains(sourceEntityId);
            if (!sourceIsDestroying && Spec.OnTargetDestroyed == TargetDestroyedPolicy.DestroySources)
            {
                sourcesToDestroy.Add(_entityKeys.GetKey(sourceEntityId));
            }

            Detach(sourceEntityId, entityId, in _links.GetReadonly(sourceEntityId), UnlinkReason.TargetDestroyed);

            if (!sourceIsDestroying && Spec.OnTargetDestroyed == TargetDestroyedPolicy.UnlinkSources && _targetEntityIdBySourceEntityId.Get(sourceEntityId) < 0)
            {
                _links.Remove(sourceEntityId);
            }
        }

        ReturnSourceEntityIdList(sourceEntityIds);
    }

    void IRelationship.OnSourceDestroying(int entityId)
    {
        if (_links.Count == 0)
        {
            return;
        }

        var targetEntityId = _targetEntityIdBySourceEntityId.Get(entityId);
        if (targetEntityId < 0)
        {
            return;
        }

        Detach(entityId, targetEntityId, in _links.GetReadonly(entityId), UnlinkReason.SourceDestroyed);
    }

    private void OnLinkWritten(int sourceEntityId, int denseIndex)
    {
        ref readonly var link = ref _links.GetReadonlyByDenseIndex(denseIndex);
        var targetEntityId = sourceEntityId == _sourceEntityIdBeingLinked
            ? _targetEntityIdBeingLinked
            : ResolveWrittenTargetOrRemoveLink(sourceEntityId, link.TargetKey);
        var previousTargetEntityId = _targetEntityIdBySourceEntityId.Get(sourceEntityId);
        if (previousTargetEntityId == targetEntityId)
        {
            return;
        }

        if (previousTargetEntityId >= 0)
        {
            Detach(sourceEntityId, previousTargetEntityId, in link, UnlinkReason.Retargeted);
        }

        _targetEntityIdBySourceEntityId.GetWritable(sourceEntityId) = targetEntityId;
        _relatedSources.Add(targetEntityId, new RelatedSourceComponent<TLink>(sourceEntityId));
    }

    private void OnLinkRemoving(int sourceEntityId, int denseIndex)
    {
        var targetEntityId = _targetEntityIdBySourceEntityId.Get(sourceEntityId);
        if (targetEntityId < 0)
        {
            return;
        }

        Detach(sourceEntityId, targetEntityId, in _links.GetReadonlyByDenseIndex(denseIndex), UnlinkReason.Unlinked);
    }

    /// <remarks>Handlers get a copy of link: one may destroy an entity, and whatever that writes can move the pool's dense storage under a reference.</remarks>
    private void Detach(int sourceEntityId, int targetEntityId, in TLink link, UnlinkReason reason)
    {
        var unlinkedLink = link;
        _targetEntityIdBySourceEntityId.GetWritable(sourceEntityId) = -1;
        _relatedSources.RemoveFirst(targetEntityId, sourceEntityId, static (ref readonly relatedSource, sourceId) => relatedSource.SourceEntityId == sourceId);
        SourceUnlinked?.Invoke(sourceEntityId, targetEntityId, in unlinkedLink, reason);
    }

    /// <summary>Resolves a link written straight to the pool; a refused one is removed, leaving the source unlinked, before the refusal is thrown.</summary>
    private int ResolveWrittenTargetOrRemoveLink(int sourceEntityId, EntityKey targetKey)
    {
        try
        {
            return ResolveTargetOrThrow(sourceEntityId, targetKey);
        }
        catch (InvalidOperationException)
        {
            _links.Remove(sourceEntityId);
            throw;
        }
    }

    /// <summary>The entity targetKey names, checked as sourceEntityId's target; the hierarchy is checked only when the target changes.</summary>
    private int ResolveTargetOrThrow(int sourceEntityId, EntityKey targetKey)
    {
        if (!_entityKeys.TryGetEntityId(targetKey, out var targetEntityId))
        {
            throw new InvalidOperationException($"{typeof(TLink).Name}: entity {sourceEntityId} can't be linked to {targetKey}, which is no living entity.");
        }

        if (_destroyingEntityIds.Contains(targetEntityId))
        {
            throw new InvalidOperationException($"{typeof(TLink).Name}: entity {sourceEntityId} can't be linked to {targetEntityId}, which is being destroyed.");
        }

        if (targetEntityId == sourceEntityId)
        {
            throw new InvalidOperationException($"{typeof(TLink).Name}: entity {sourceEntityId} can't be linked to itself.");
        }

        if (Spec.Acyclic && _targetEntityIdBySourceEntityId.Get(sourceEntityId) != targetEntityId)
        {
            ThrowIfBreaksHierarchy(sourceEntityId, targetEntityId);
        }

        return targetEntityId;
    }

    /// <summary>Throws if linking sourceEntityId to targetEntityId would close a cycle, or make any chain through it -- its ancestors above, its descendants below -- longer than MaximumDepth.</summary>
    private void ThrowIfBreaksHierarchy(int sourceEntityId, int targetEntityId)
    {
        var linksAboveSource = 0;
        for (var ancestorEntityId = targetEntityId; ancestorEntityId >= 0; ancestorEntityId = _targetEntityIdBySourceEntityId.Get(ancestorEntityId))
        {
            if (ancestorEntityId == sourceEntityId)
            {
                throw new InvalidOperationException($"{typeof(TLink).Name}: linking entity {sourceEntityId} to {targetEntityId} would close a cycle.");
            }

            if (++linksAboveSource > Spec.MaximumDepth)
            {
                ThrowChainTooLong(sourceEntityId, targetEntityId);
            }
        }

        var linksAllowedBelowSource = Spec.MaximumDepth - linksAboveSource;
        if (LinksBelow(sourceEntityId, linksAllowedBelowSource) > linksAllowedBelowSource)
        {
            ThrowChainTooLong(sourceEntityId, targetEntityId);
        }
    }

    /// <summary>The longest chain of links from entityId's sources down, 0 for an entity nothing is linked to -- or, once it passes linksAllowed, any length past it.</summary>
    /// <remarks>Stops at the first chain longer than linksAllowed, so a large branch costs only as deep as the cap matters.</remarks>
    private int LinksBelow(int entityId, int linksAllowed)
    {
        var longestChain = 0;
        for (var denseIndex = _relatedSources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _relatedSources.GetNextDenseIndex(denseIndex))
        {
            if (linksAllowed == 0)
            {
                return 1;
            }

            longestChain = System.Math.Max(longestChain, 1 + LinksBelow(_relatedSources.GetReadonlyByDenseIndex(denseIndex).SourceEntityId, linksAllowed - 1));
            if (longestChain > linksAllowed)
            {
                return longestChain;
            }
        }

        return longestChain;
    }

    private void ThrowChainTooLong(int sourceEntityId, int targetEntityId) =>
        throw new InvalidOperationException($"{typeof(TLink).Name}: linking entity {sourceEntityId} to {targetEntityId} would make a chain longer than {Spec.MaximumDepth}.");

    private void ThrowIfNotAcyclic()
    {
        if (!Spec.Acyclic)
        {
            throw new InvalidOperationException($"{typeof(TLink).Name} isn't acyclic, so it has no roots or descendants to walk.");
        }
    }

    private List<int> RentSourceEntityIdList() => _sourceEntityIdListPool.TryPop(out var list) ? list : [];

    private void ReturnSourceEntityIdList(List<int> list)
    {
        list.Clear();
        _sourceEntityIdListPool.Push(list);
    }
}
