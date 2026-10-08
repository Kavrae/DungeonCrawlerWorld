using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Places auras on tiles, as anchor entities, and ends them: when their last source goes, when their owner is destroyed, and when they are.</summary>
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
/// safe. Its owner being destroyed takes its sources away at once. The anchor itself being destroyed
/// while a toggle holds it -- its neighborhood unloading -- switches that toggle off, so a toggle is
/// never on with nothing behind it.
/// </para>
/// </remarks>
/// <param name="spawnAnchor">Spawns an AuraAnchor at a tile, returning its id, or a negative id when it couldn't be placed.</param>
public sealed class AuraAnchors(ComponentManager componentManager, EntityManager entityManager, AuraSources auraSources, Func<Vector3Int, int> spawnAnchor)
{
    private readonly Queue<EntityKey> _emptiedAnchors = new();
    private readonly List<int> _ownedAnchorsScratch = [];

    private PackedComponentPool<AuraAnchorComponent> Anchors => componentManager.GetPackedPool<AuraAnchorComponent>();

    /// <summary>Places an anchor radiating aura at tile, returning its id, or a negative id when the tile can't hold one.</summary>
    /// <param name="ownerEntityId">The entity that placed it, which ends it by being destroyed; null for none.</param>
    /// <param name="heldGrantKey">The owner's toggle holding it, which ends it by reverting; null for an anchor no toggle holds.</param>
    /// <param name="expiresAtFrame">When a timed anchor's source expires; null for one that lasts until something ends it.</param>
    public int Place(Vector3Int tile, AuraDefinition aura, ushort power, byte size, ActionSource placedBy, int? ownerEntityId, uint? heldGrantKey, uint? expiresAtFrame)
    {
        var anchorId = spawnAnchor(tile);
        if (anchorId < 0)
        {
            return anchorId;
        }

        var ownerKey = ownerEntityId is { } owner ? entityManager.Keys.GetKey(owner) : EntityKey.None;
        componentManager.Merge(anchorId, new DisplayTextComponent($"{aura.Name} Aura", $"A {aura.Name} aura, cast on this spot."));
        Anchors.Add(anchorId, new AuraAnchorComponent(placedBy, ownerKey, heldGrantKey ?? AuraSourceComponent.NoHeldGrantKey));

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
        var ownerKey = entityManager.Keys.GetKey(ownerEntityId);
        CollectOwnedAnchors(ownerKey, heldGrantKey);
        foreach (var anchorId in _ownedAnchorsScratch)
        {
            auraSources.RemoveAll(anchorId);
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

    /// <summary>An entity is being destroyed: if it owns anchors, they lose their sources; if it is an anchor a toggle holds, that toggle is switched off.</summary>
    /// <param name="switchOffToggle">Switches off the holder's toggle with the given key.</param>
    public void OnEntityDestroying(int entityId, Action<int, uint> switchOffToggle)
    {
        var anchors = Anchors;
        if (anchors.TryGetReadonly(entityId, out var anchor))
        {
            if (anchor.HeldGrantKey != AuraSourceComponent.NoHeldGrantKey &&
                entityManager.Keys.TryGetEntityId(anchor.OwnerKey, out var holderId) &&
                !entityManager.IsDestroying(holderId))
            {
                switchOffToggle(holderId, anchor.HeldGrantKey);
            }

            return;
        }

        if (anchors.Count == 0)
        {
            return;
        }

        CollectOwnedAnchors(entityManager.Keys.GetKey(entityId), heldGrantKey: null);
        foreach (var anchorId in _ownedAnchorsScratch)
        {
            auraSources.RemoveAll(anchorId);
        }
    }

    /// <summary>Fills _ownedAnchorsScratch with the anchors ownerKey owns -- those heldGrantKey holds, or all of them for null -- skipping any already being destroyed.</summary>
    private void CollectOwnedAnchors(EntityKey ownerKey, uint? heldGrantKey)
    {
        _ownedAnchorsScratch.Clear();
        if (ownerKey.IsNone)
        {
            return;
        }

        var anchors = Anchors;
        var anchorIds = anchors.EntityIds;
        var components = anchors.Components;
        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = components[index];
            if (anchor.OwnerKey == ownerKey && (heldGrantKey is null || anchor.HeldGrantKey == heldGrantKey) && !entityManager.IsDestroying(anchorIds[index]))
            {
                _ownedAnchorsScratch.Add(anchorIds[index]);
            }
        }
    }
}
