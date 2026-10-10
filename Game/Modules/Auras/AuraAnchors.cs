using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Relationships;
using Engine.Math;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Places auras on tiles, as anchor entities, and ends them when a toggle holding them turns off or their last source goes.</summary>
/// <remarks>
/// <para>
/// An aura granted at a tile rather than to an entity (AuraSourceGrant with no target entity) is an
/// AuraAnchor entity there carrying the source -- an ordinary entity, so the aura field, the tiers and
/// the streamer treat it like any other. A timed one carries an AuraSourceExpiryComponent; one a toggle
/// holds carries its source under the toggle's key, and is ended by the toggle reverting (EndHeld).
/// </para>
/// <para>
/// An anchor with no source left is destroyed by AuraAnchorEndingSystem rather than at once: the last
/// source is often removed inside another system's timer callback, where destroying an entity is not
/// safe. Its owner, if it has one, is its AuraAnchorOwnerLink, so the relationship destroys it with the
/// owner and finds an owner's anchors without a scan.
/// </para>
/// </remarks>
/// <param name="spawnAnchor">Spawns an AuraAnchor at a tile, returning its id, or a negative id when it couldn't be placed.</param>
public sealed class AuraAnchors(ComponentManager componentManager, EntityManager entityManager, AuraSources auraSources, Func<Vector3Int, int> spawnAnchor)
{
    private readonly Queue<EntityKey> _emptiedAnchors = new();
    private readonly List<int> _ownedAnchorsScratch = [];

    private PackedComponentPool<AuraAnchorComponent> Anchors => componentManager.GetPackedPool<AuraAnchorComponent>();

    private Relationship<AuraAnchorOwnerLink> OwnerLinks => componentManager.GetRelationship<AuraAnchorOwnerLink>();

    /// <summary>Places an anchor radiating aura at tile, returning its id, or a negative id when the tile can't hold one.</summary>
    /// <param name="ownerEntityId">The entity that placed it, which ends it by being destroyed; null for none.</param>
    /// <param name="heldGrantKey">The owner's toggle holding it, which ends it by reverting; null for an anchor no toggle holds.</param>
    /// <param name="expiresAtFrame">When a timed anchor's source expires; null for one that lasts until something ends it.</param>
    /// <remarks>An owner being destroyed can't own an anchor (its link would be refused), so none is placed for one.</remarks>
    public int Place(Vector3Int tile, AuraDefinition aura, ushort power, byte size, ActionSource placedBy, int? ownerEntityId, uint? heldGrantKey, uint? expiresAtFrame)
    {
        if (ownerEntityId is { } placingOwner && entityManager.IsDestroying(placingOwner))
        {
            return -1;
        }

        var anchorId = spawnAnchor(tile);
        if (anchorId < 0)
        {
            return anchorId;
        }

        componentManager.Merge(anchorId, new DisplayTextComponent($"{aura.Name} Aura", $"A {aura.Name} aura, cast on this spot."));
        Anchors.Add(anchorId, new AuraAnchorComponent(placedBy));
        if (ownerEntityId is { } owner)
        {
            OwnerLinks.Link(anchorId, new AuraAnchorOwnerLink(entityManager.Keys.GetKey(owner), heldGrantKey ?? AuraSourceComponent.NoHeldGrantKey));
        }

        if (heldGrantKey is { } key)
        {
            auraSources.AddHeld(anchorId, aura, power, size, key);
        }
        else
        {
            auraSources.Apply(anchorId, aura, power, size);
        }

        if (expiresAtFrame is { } expires)
        {
            componentManager.Merge(anchorId, new AuraSourceExpiryComponent(auraSources.GetId(aura), expires));
        }

        return anchorId;
    }

    /// <summary>Takes away the anchors ownerEntityId's toggle heldGrantKey holds: their sources at once, the entities once AuraAnchorEndingSystem next runs.</summary>
    public void EndHeld(int ownerEntityId, uint heldGrantKey)
    {
        var ownerLinks = OwnerLinks;
        ownerLinks.CopySourceEntityIds(ownerEntityId, _ownedAnchorsScratch);
        foreach (var anchorId in _ownedAnchorsScratch)
        {
            if (ownerLinks.Links.GetReadonly(anchorId).HeldGrantKey == heldGrantKey && !entityManager.IsDestroying(anchorId))
            {
                auraSources.RemoveAll(anchorId);
            }
        }
    }

    /// <summary>Queues entityId for ending if it is an anchor with no source left -- called for every source removed.</summary>
    public void OnSourceRemoved(int entityId)
    {
        if (Anchors.Has(entityId) && !auraSources.Radiates(entityId) && !entityManager.IsDestroying(entityId))
        {
            _emptiedAnchors.Enqueue(entityManager.Keys.GetKey(entityId));
        }
    }

    /// <summary>Destroys every anchor queued since the last call that still has no source.</summary>
    public void EndEmptiedAnchors()
    {
        while (_emptiedAnchors.TryDequeue(out var anchorKey))
        {
            if (entityManager.Keys.TryGetEntityId(anchorKey, out var anchorId) && Anchors.Has(anchorId) && !auraSources.Radiates(anchorId))
            {
                entityManager.DestroyEntity(anchorId);
            }
        }
    }

    /// <summary>Whether an anchor's link ending means the toggle holding it must switch off: the anchor was destroyed while its owner lives on.</summary>
    /// <remarks>An owner being destroyed takes its toggles with it, whether its anchors reach it as TargetDestroyed or, destroyed by something else during that destroy, as SourceDestroyed.</remarks>
    public bool EndsHoldingToggle(int ownerEntityId, in AuraAnchorOwnerLink link, UnlinkReason reason) =>
        reason == UnlinkReason.SourceDestroyed && link.HeldGrantKey != AuraSourceComponent.NoHeldGrantKey && !entityManager.IsDestroying(ownerEntityId);
}
