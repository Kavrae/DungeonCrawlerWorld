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
/// builds the source-fixed half of an ActionEffectContext (ActivatorTags: action.Tags, for
/// DirectDamage's ability-score bonus), walks target tiles via IMapQuery.GetOccupantEntityIdsAt,
/// and calls ActionEffectSequence.Apply(action.Effects, ...) once per resolved target. Contains
/// no per-effect-kind knowledge at all -- what an action's effects actually do lives entirely on
/// the ActionEffect/IActionEffectEntry types themselves. Takes the already-resolved action
/// (ActionInstanceQueries.TryResolveEffectiveAction, not a raw ActionCatalog lookup) so a
/// per-instance Override -- e.g. a flat damage number, see ActionInstanceComponent's own doc
/// comment -- is already baked into action.Effects by the time this runs.
/// </summary>
public static class ActionEffectResolver
{
    public static void Apply(
        ActionDefinition action,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        IMapQuery mapQuery,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<AbilityScoreComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null)
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
            BodyParts: bodyParts,
            PlayerQuery: playerQuery);

        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in mapQuery.GetOccupantEntityIdsAt(tile))
            {
                ActionEffectSequence.Apply(action.Effects, context with { TargetEntityId = targetEntityId });
            }
        }
    }
}
