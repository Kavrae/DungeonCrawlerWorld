using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects;

/// <summary>
/// The one write surface for immunities -- the counterpart to StatusEffectImmunity.IsImmune's
/// read surface. Everything that grants one goes through here (StatusEffectImmunityGrant, the
/// blueprints that make an object immune by construction) so an entity never ends up holding two
/// instances of the same StatusEffectType: the timer wheel names an immunity by (entity,
/// EffectType), so a duplicate type would give two timers one identity and let either deadline
/// resolve to the wrong instance.
/// </summary>
public static class StatusEffectImmunityEffects
{
    /// <summary>
    /// Makes entityId immune to effectType until expiresAtFrame (FrameDeadline.Never for
    /// permanent). An entity already immune to that type keeps the later of the two deadlines
    /// rather than gaining a second instance -- so re-granting only ever extends an immunity,
    /// and a permanent one is never shortened by a timed grant (Never is the largest deadline).
    /// The same "refreshing merges into what's already running" rule ParalysisEffects.Apply uses.
    /// </summary>
    public static void Grant(MultiComponentPool<StatusEffectImmunityComponent> immunities, int entityId, StatusEffectType effectType, uint expiresAtFrame)
    {
        var extended = immunities.TryUpdateFirst(
            entityId,
            (Type: effectType, Deadline: expiresAtFrame),
            static (ref readonly StatusEffectImmunityComponent immunity, (StatusEffectType Type, uint Deadline) state) => immunity.EffectType == state.Type,
            static (ref StatusEffectImmunityComponent immunity, (StatusEffectType Type, uint Deadline) state) =>
                immunity.ExpiresAtFrame = System.Math.Max(immunity.ExpiresAtFrame, state.Deadline));

        if (!extended)
        {
            immunities.Add(entityId, new StatusEffectImmunityComponent(effectType, expiresAtFrame));
        }
    }

    /// <summary>Grant with no expiry -- for an entity that is immune by what it is (see TreasureChest/Shop), not by something that was cast on it.</summary>
    public static void GrantPermanent(MultiComponentPool<StatusEffectImmunityComponent> immunities, int entityId, StatusEffectType effectType) =>
        Grant(immunities, entityId, effectType, FrameDeadline.Never);
}
