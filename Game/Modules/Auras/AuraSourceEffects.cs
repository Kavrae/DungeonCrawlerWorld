using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Auras.Components;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Write surface for granting and revoking an AuraSourceComponent.</summary>
/// <remarks>
/// Every removal goes through here, because a pool announces nothing when an instance is removed:
/// each publishes AuraSourceRemovedEvent, which is how AuraSystem takes the source out of the aura
/// field. An add needs no event -- AuraSystem observes the pool -- so a blueprint may also add a
/// source directly. A source is never changed in place: Apply replaces one by removing and adding.
/// </remarks>
/// <remarks>Auras are named by session-local id here; AuraSources is the same surface for a caller holding a Guid.</remarks>
public static class AuraSourceEffects
{
    /// <summary>
    /// Toggles one aura's source on entityId: removes the entity's existing source of that aura if
    /// it has one, otherwise adds a new one -- matches AuraSourceGrant's permanent-mode "on/off
    /// switch" semantics. Scoped to the aura, not "remove whatever this entity has": an entity can
    /// radiate more than one aura (MultiComponentPool), so toggling Burning off must not also clear
    /// an unrelated Poison source the same entity happens to carry.
    /// </summary>
    public static void Toggle(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, byte strength)
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var existing = sources.GetReadonlyByDenseIndex(denseIndex);
            if (existing.AuraId == auraId)
            {
                sources.RemoveByDenseIndex(denseIndex);
                eventBus.Publish(new AuraSourceRemovedEvent(entityId, existing));
                return;
            }
        }

        sources.Add(entityId, new AuraSourceComponent(auraId, strength));
    }

    /// <summary>
    /// Ensures entityId carries exactly one source of the aura at this strength -- adds
    /// fresh if none exists, or replaces (Revoke-then-add, never an in-place mutation, which nothing observing the pool could tell from an add) if one already does. Distinct from Toggle's
    /// flip semantics: re-calling Apply on an already-present source refreshes it (e.g. resets a
    /// caller-tracked expiry) rather than switching it off -- the shape AuraSourceGrant's
    /// timed (DurationFrames-bearing) usage needs, since a flip would extinguish an existing
    /// grant instead of renewing it. Named Apply, not Grant, so it doesn't collide with
    /// AuraSourceGrant's own name -- the entry is a noun (a grant), this is the verb performed on
    /// it, the same split every other *Effects write-surface uses (see StatModifierEffects.Apply).
    /// </summary>
    public static void Apply(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, byte strength)
    {
        Revoke(sources, eventBus, entityId, auraId);

        sources.Add(entityId, new AuraSourceComponent(auraId, strength));
    }

    /// <summary>Removes entityId's source of the aura if present -- unconditional (unlike Toggle, never adds one if absent). Used by AuraSourceExpirySystem once a timed grant's duration runs out, and by Apply above (revoke-then-add) to refresh an existing one.</summary>
    public static void Revoke(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId)
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var existing = sources.GetReadonlyByDenseIndex(denseIndex);
            if (existing.AuraId == auraId)
            {
                sources.RemoveByDenseIndex(denseIndex);
                eventBus.Publish(new AuraSourceRemovedEvent(entityId, existing));
                return;
            }
        }
    }

    /// <summary>
    /// Removes every aura source entityId carries, publishing one AuraSourceRemovedEvent per
    /// instance -- used by DeathSystem so a creature that dies while an aura is still toggled on
    /// doesn't keep radiating it from its corpse forever (corpses persist indefinitely and are
    /// never fully destroyed, see DeathSystem's own doc comment). Re-reads the entity's first
    /// remaining dense index after each removal rather than walking a cached chain, since
    /// removing an instance invalidates the chain pointers a stale walk would otherwise rely on.
    /// </summary>
    public static void RemoveAll(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId)
    {
        while (sources.GetFirstDenseIndex(entityId) is var denseIndex && denseIndex != -1)
        {
            var removed = sources.GetReadonlyByDenseIndex(denseIndex);
            sources.RemoveByDenseIndex(denseIndex);
            eventBus.Publish(new AuraSourceRemovedEvent(entityId, removed));
        }
    }
}
