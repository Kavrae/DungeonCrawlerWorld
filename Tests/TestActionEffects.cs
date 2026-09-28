using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests;

/// <summary>Resolves an action, or builds an ActionEffectContext, for a test that only cares about some of the pools involved, filling in the rest with EmptyPools.</summary>
internal static class TestActionEffects
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
        EntityKeys entityKeys,
        long now,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null,
        ProcessingTierQuery? processingTiers = null,
        BlueprintRegistry? creatures = null,
        PackedComponentPool<ManaComponent>? mana = null) =>
        ActionEffectResolver.Apply(action, sourceEntityId, targetTiles, mapQuery, health, eventBus, mathUtility, playerQuery ?? TestPlayerQuery.NoPlayer, statusEffectAppliers, componentManager, entityKeys, now,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            abilityScores ?? EmptyPools.Packed<AbilityScoresComponent>(),
            mana ?? EmptyPools.Packed<ManaComponent>(),
            auraSources ?? EmptyPools.Multi<StatusEffectAuraSourceComponent>(),
            hotkeyExpansionUnlocks ?? EmptyPools.Packed<HotkeyExpansionUnlockComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            dodgingEntities ?? EmptyPools.Packed<DodgingComponent>(),
            processingTiers ?? EmptyPools.Tiers(),
            creatures ?? new BlueprintRegistry());

    public static ActionEffectContext Context(
        int SourceEntityId,
        int TargetEntityId,
        PackedComponentPool<SimpleHealthComponent> Health,
        EventBus EventBus,
        MathUtility MathUtility,
        ComponentManager ComponentManager,
        EntityKeys EntityKeys,
        string ActivatorName,
        IReadOnlyList<Tag> ActivatorTags,
        long Now,
        MultiComponentPool<StatModifierComponent>? StatModifiers = null,
        PackedComponentPool<AbilityScoresComponent>? AbilityScores = null,
        PackedComponentPool<ManaComponent>? Mana = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? HotkeyExpansionUnlocks = null,
        StatusEffectAuraApplierRegistry? StatusEffectAppliers = null,
        PackedComponentPool<DeadComponent>? DeadEntities = null,
        BlueprintRegistry? Definitions = null,
        MultiComponentPool<StatusEffectAuraSourceComponent>? AuraSources = null,
        EntityBodyParts? BodyParts = null,
        IPlayerQuery? PlayerQuery = null,
        float DurationScaleMultiplier = 1.0f,
        byte ChainDepth = 0) =>
        new(SourceEntityId, TargetEntityId, Health, EventBus, MathUtility, ComponentManager, EntityKeys, ActivatorName, ActivatorTags, Now,
            StatModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            AbilityScores ?? EmptyPools.Packed<AbilityScoresComponent>(),
            Mana ?? EmptyPools.Packed<ManaComponent>(),
            HotkeyExpansionUnlocks ?? EmptyPools.Packed<HotkeyExpansionUnlockComponent>(),
            DeadEntities ?? EmptyPools.Packed<DeadComponent>(),
            AuraSources ?? EmptyPools.Multi<StatusEffectAuraSourceComponent>(),
            BodyParts ?? EmptyPools.BodyParts(),
            PlayerQuery ?? TestPlayerQuery.NoPlayer,
            StatusEffectAppliers ?? new StatusEffectAuraApplierRegistry(), Definitions ?? new BlueprintRegistry(), DurationScaleMultiplier, ChainDepth);
}
