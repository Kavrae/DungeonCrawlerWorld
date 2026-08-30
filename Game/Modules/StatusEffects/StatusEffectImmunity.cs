using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>The one chokepoint "is entityId currently immune to effectType" -- every ApplyStack implementation (PoisonEffects, BurningEffects, BurningAuraApplier's body-part-scoped path, ParalysisEffects) checks this before adding a stack.</summary>
public static class StatusEffectImmunity
{
    /// <summary>
    /// source/eventBus/playerQuery are only needed to publish StatusEffectImmunityBlockedEvent
    /// when this call actually blocks something -- both eventBus and playerQuery are optional
    /// (most low-level callers/tests have no need to observe a block), and the publish itself is
    /// further gated on the player being involved as either entityId or source, mirroring
    /// HealthHeal.PublishHealEvent's identical shape.
    /// </summary>
    public static bool IsImmune(ComponentManager componentManager, int entityId, StatusEffectType effectType, StatusEffectSource source = default, EventBus? eventBus = null, IPlayerQuery? playerQuery = null)
    {
        if (!componentManager.IsRegistered<StatusEffectImmunityComponent>())
        {
            return false;
        }

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        var immune = false;
        for (var denseIndex = immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = immunities.GetNextDenseIndex(denseIndex))
        {
            if (immunities.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                immune = true;
                break;
            }
        }

        if (immune)
        {
            PublishBlockedEvent(eventBus, playerQuery, entityId, effectType, source);
        }

        return immune;
    }

    private static void PublishBlockedEvent(EventBus? eventBus, IPlayerQuery? playerQuery, int entityId, StatusEffectType effectType, StatusEffectSource source)
    {
        if (eventBus is null || playerQuery is null)
        {
            return;
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId || (source.Kind == StatusEffectSourceKind.Entity && source.EntityId == playerQuery.PlayerEntityId);
        if (!playerInvolved)
        {
            return;
        }

        eventBus.Publish(new StatusEffectImmunityBlockedEvent(entityId, effectType, source));
    }
}
