using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Auras.Components;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Write surface for granting and revoking an AuraSourceComponent.</summary>
/// <remarks>
/// <para>
/// Every removal goes through here, because AuraSystem takes a source out of the aura field on
/// AuraSourceRemovedEvent, which each removal publishes. An add needs no event -- AuraSystem observes
/// the pool -- so a blueprint may also add a source directly. A source is never changed in place:
/// Apply replaces one by removing and adding.
/// </para>
/// <para>
/// An entity may hold several sources of one aura. Apply and Revoke act only on the one with no key;
/// a source a toggle holds (AddHeld) is removed only under that toggle's key (RemoveHeld), so a timed
/// grant expiring never ends a toggle's source and one toggle going off never ends another's.
/// </para>
/// <para>Auras are named by session-local id here; AuraSources is the same surface for a caller holding a definition.</para>
/// </remarks>
public static class AuraSourceEffects
{
    /// <summary>Ensures entityId carries exactly one unkeyed source of the aura, at this power and size: adds one if it has none, replaces the one it has otherwise.</summary>
    /// <remarks>
    /// Replaces by removing and adding, never in place, which nothing observing the pool could tell
    /// from an add. Re-applying therefore refreshes: a timed grant renews its source rather than
    /// ending it. Named Apply, not Grant, so it doesn't collide with AuraSourceGrant: the entry is the
    /// noun and this is the verb performed on it.
    /// </remarks>
    public static void Apply(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, ushort power, byte size)
    {
        Revoke(sources, eventBus, entityId, auraId);

        sources.Add(entityId, new AuraSourceComponent(auraId, power, size));
    }

    /// <summary>Removes entityId's unkeyed source of the aura, if it has one.</summary>
    public static void Revoke(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId) =>
        RemoveFirstMatching(sources, eventBus, entityId, auraId, AuraSourceComponent.NoHeldGrantKey);

    /// <summary>Adds a source of the aura held under heldGrantKey, beside any other source of that aura entityId has.</summary>
    public static void AddHeld(MultiComponentPool<AuraSourceComponent> sources, int entityId, byte auraId, ushort power, byte size, uint heldGrantKey)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(heldGrantKey, AuraSourceComponent.NoHeldGrantKey);

        sources.Add(entityId, new AuraSourceComponent(auraId, power, size, heldGrantKey));
    }

    /// <summary>Removes entityId's source of the aura held under heldGrantKey, if it has one.</summary>
    public static void RemoveHeld(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, uint heldGrantKey)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(heldGrantKey, AuraSourceComponent.NoHeldGrantKey);

        RemoveFirstMatching(sources, eventBus, entityId, auraId, heldGrantKey);
    }

    private static void RemoveFirstMatching(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, uint heldGrantKey)
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var existing = sources.GetReadonlyByDenseIndex(denseIndex);
            if (existing.AuraId == auraId && existing.HeldGrantKey == heldGrantKey)
            {
                sources.RemoveByDenseIndex(denseIndex);
                eventBus.Publish(new AuraSourceRemovedEvent(entityId, existing));
                return;
            }
        }
    }

    /// <summary>Removes every source entityId carries, keyed or not, publishing one AuraSourceRemovedEvent per instance.</summary>
    /// <remarks>
    /// For an entity being destroyed. Re-reads the entity's first remaining dense index after each
    /// removal rather than walking a cached chain, since removing an instance invalidates the chain
    /// pointers a stale walk would rely on.
    /// </remarks>
    public static void RemoveAll(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId)
    {
        while (sources.GetFirstDenseIndex(entityId) is var denseIndex && denseIndex != -1)
        {
            var removed = sources.GetReadonlyByDenseIndex(denseIndex);
            sources.RemoveByDenseIndex(denseIndex);
            eventBus.Publish(new AuraSourceRemovedEvent(entityId, removed));
        }
    }

    /// <summary>Removes every source entityId carries that no toggle holds, publishing one AuraSourceRemovedEvent per instance.</summary>
    /// <remarks>
    /// For an entity that died: a blueprint's aura and a timed grant's end with it, so a corpse
    /// doesn't radiate them for as long as it lies there. What a toggle holds is the toggle's to end.
    /// Restarts the walk after each removal, for the reason RemoveAll re-reads the first index.
    /// </remarks>
    public static void RemoveUnheld(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId)
    {
        var denseIndex = sources.GetFirstDenseIndex(entityId);
        while (denseIndex != -1)
        {
            var source = sources.GetReadonlyByDenseIndex(denseIndex);
            if (source.HeldGrantKey != AuraSourceComponent.NoHeldGrantKey)
            {
                denseIndex = sources.GetNextDenseIndex(denseIndex);
                continue;
            }

            sources.RemoveByDenseIndex(denseIndex);
            eventBus.Publish(new AuraSourceRemovedEvent(entityId, source));
            denseIndex = sources.GetFirstDenseIndex(entityId);
        }
    }
}
