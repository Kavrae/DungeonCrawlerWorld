using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// Write surface for granting/revoking a StatusEffectAuraSourceComponent -- the only place that
/// mutates that pool outside of blueprint-time population, so it's also the only place that
/// needs to remember to publish AuraSourceAddedEvent/AuraSourceRemovedEvent alongside the
/// mutation (StatusEffectAuraSystem/MapTintGrid both subscribe to keep their own incrementally-
/// maintained grids in sync -- see either's own doc comment).
/// </summary>
public static class AuraSourceEffects
{
    /// <summary>
    /// Toggles one EffectType's aura source on entityId: removes the entity's existing source of
    /// that type if it has one, otherwise adds a new one -- matches AuraSourceGrant's permanent-mode
    /// "on/off switch" semantics. Type-scoped, not "remove whatever this entity has": an entity
    /// can carry more than one simultaneous aura type (MultiComponentPool), so toggling Burning
    /// off must not also clear an unrelated Poison source the same entity happens to carry.
    /// </summary>
    public static void Toggle(MultiComponentPool<StatusEffectAuraSourceComponent> sources, EventBus eventBus, int entityId, StatusEffectType type, int auraAndGlowStrength, Color glowColor)
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var existing = sources.GetReadonlyByDenseIndex(denseIndex);
            if (existing.EffectType == type)
            {
                sources.RemoveByDenseIndex(denseIndex);
                eventBus.Publish(new AuraSourceRemovedEvent(entityId, existing));
                return;
            }
        }

        var source = new StatusEffectAuraSourceComponent(type, auraAndGlowStrength, glowColor);
        sources.Add(entityId, source);
        eventBus.Publish(new AuraSourceAddedEvent(entityId, source));
    }

    /// <summary>
    /// Ensures entityId carries exactly one aura source of type with these parameters -- adds
    /// fresh if none exists, or replaces (Revoke-then-add, so listeners see a clean removed-then-
    /// added pair rather than an in-place mutation) if one already does. Distinct from Toggle's
    /// flip semantics: re-calling Apply on an already-present source refreshes it (e.g. resets a
    /// caller-tracked expiry) rather than switching it off -- the shape AuraSourceGrant's
    /// timed (DurationFrames-bearing) usage needs, since a flip would extinguish an existing
    /// grant instead of renewing it. Named Apply, not Grant, so it doesn't collide with
    /// AuraSourceGrant's own name -- the entry is a noun (a grant), this is the verb performed on
    /// it, the same split every other *Effects write-surface uses (see StatModifierEffects.Apply).
    /// </summary>
    public static void Apply(MultiComponentPool<StatusEffectAuraSourceComponent> sources, EventBus eventBus, int entityId, StatusEffectType type, int auraAndGlowStrength, Color glowColor)
    {
        Revoke(sources, eventBus, entityId, type);

        var source = new StatusEffectAuraSourceComponent(type, auraAndGlowStrength, glowColor);
        sources.Add(entityId, source);
        eventBus.Publish(new AuraSourceAddedEvent(entityId, source));
    }

    /// <summary>Removes entityId's aura source of type if present -- unconditional (unlike Toggle, never adds one if absent). Used by AuraSourceExpirySystem once a timed grant's duration runs out, and by Apply above (revoke-then-add) to refresh an existing one.</summary>
    public static void Revoke(MultiComponentPool<StatusEffectAuraSourceComponent> sources, EventBus eventBus, int entityId, StatusEffectType type)
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var existing = sources.GetReadonlyByDenseIndex(denseIndex);
            if (existing.EffectType == type)
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
    public static void RemoveAll(MultiComponentPool<StatusEffectAuraSourceComponent> sources, EventBus eventBus, int entityId)
    {
        while (sources.GetFirstDenseIndex(entityId) is var denseIndex && denseIndex != -1)
        {
            var removed = sources.GetReadonlyByDenseIndex(denseIndex);
            sources.RemoveByDenseIndex(denseIndex);
            eventBus.Publish(new AuraSourceRemovedEvent(entityId, removed));
        }
    }
}
