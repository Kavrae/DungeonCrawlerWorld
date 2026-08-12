using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Per-activation orchestration shared by ActionActivationSystem (Immediate/FreeCast) and
/// DelayedActionSystem (a Delayed action's windup completing) -- publishes ActionActivatedEvent,
/// builds the source-fixed half of an ActionEffectContext (DamageOverride: instance.DamageAmount,
/// the per-instance/per-race override -- see ActionInstanceComponent's own doc comment;
/// ActivatorTags: action.Tags, for DamageEffectEntry's ability-score bonus), walks target tiles
/// via TargetResolution, and calls ActionEffectSequence.Apply(action.Effects, ...) once per
/// resolved target. Contains no per-effect-kind knowledge at all -- what an action's effects
/// actually do lives entirely on the ActionEffect/IActionEffectEntry types themselves.
/// </summary>
public static class ActionEffectResolver
{
    public static void Apply(
        ActionDefinition action,
        ActionInstanceComponent instance,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        IMapQuery mapQuery,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null)
    {
        eventBus.Publish(new ActionActivatedEvent(sourceEntityId, action.Id));

        var context = new ActionEffectContext(
            SourceEntityId: sourceEntityId,
            TargetEntityId: sourceEntityId,
            Health: health,
            EventBus: eventBus,
            MathUtility: mathUtility,
            ComponentManager: componentManager,
            ActivatorName: action.Name,
            ActivatorTags: action.Tags,
            StatModifiers: statModifiers,
            AbilityScores: abilityScores,
            HotkeyExpansionUnlocks: hotkeyExpansionUnlocks,
            StatusEffectAppliers: statusEffectAppliers,
            DeadEntities: deadEntities,
            AuraSources: auraSources,
            PlayerQuery: playerQuery,
            DamageOverride: instance.DamageAmount > 0 ? instance.DamageAmount : null);

        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in TargetResolution.EnumerateTargets(tile, mapQuery))
            {
                ActionEffectSequence.Apply(action.Effects, context with { TargetEntityId = targetEntityId });
            }
        }
    }
}
