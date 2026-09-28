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
    /// when this call actually blocks something, and only when the player is involved as either entityId or source, mirroring
    /// HealthHeal.PublishHealEvent's identical shape.
    /// </summary>
    public static bool IsImmune(ComponentManager componentManager, int entityId, StatusEffectType effectType, ActionSource source, EventBus eventBus, IPlayerQuery playerQuery)
    {
        var immune = HasImmunity(componentManager, entityId, effectType);

        if (immune)
        {
            PublishBlockedEvent(eventBus, playerQuery, entityId, effectType, source);
        }

        return immune;
    }

    /// <summary>Whether entityId is currently immune to effectType, without reporting a blocked grant.</summary>
    public static bool HasImmunity(ComponentManager componentManager, int entityId, StatusEffectType effectType)
    {
        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        for (var denseIndex = immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = immunities.GetNextDenseIndex(denseIndex))
        {
            if (immunities.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                return true;
            }
        }

        return false;
    }

    private static void PublishBlockedEvent(EventBus eventBus, IPlayerQuery playerQuery, int entityId, StatusEffectType effectType, ActionSource source)
    {
        var playerInvolved = entityId == playerQuery.PlayerEntityId || source.IsEntity(playerQuery.PlayerEntityKey);
        if (!playerInvolved)
        {
            return;
        }

        eventBus.Publish(new StatusEffectImmunityBlockedEvent(entityId, effectType, source));
    }
}
