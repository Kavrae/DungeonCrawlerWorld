using Engine.ECS.Components;
using Game.Effects;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Engine.Tags;
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
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests;

/// <summary>Resolves an action, or builds an EffectContext, for a test that only cares about some of the pools involved, filling in the rest with EmptyPools.</summary>
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
        StatusEffectApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        EntityKeys entityKeys,
        long now,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        MultiComponentPool<AuraSourceComponent>? auraSources = null,
        AuraCatalog? auras = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null,
        ProcessingTierQuery? processingTiers = null,
        BlueprintRegistry? creatures = null,
        PackedComponentPool<ManaComponent>? mana = null,
        FloatingTextFeed? floatingTextFeed = null,
        ResolvedTargets resolved = default) =>
        ActionEffectResolver.Apply(action, sourceEntityId, targetTiles, resolved,
            Services(componentManager, entityKeys, eventBus, mathUtility, health, playerQuery, statusEffectAppliers, statModifiers, deadEntities, abilityScores, mana, hotkeyExpansionUnlocks, auraSources, auras, bodyParts, creatures, floatingTextFeed),
            mapQuery, now,
            dodgingEntities ?? EmptyPools.Packed<DodgingComponent>(),
            processingTiers ?? EmptyPools.Tiers());

    public static EffectContext Context(
        int SourceEntityId,
        int TargetEntityId,
        PackedComponentPool<SimpleHealthComponent> Health,
        EventBus EventBus,
        MathUtility MathUtility,
        ComponentManager ComponentManager,
        EntityKeys EntityKeys,
        string ActivatorName,
        GameplayTagSet ActivatorTags,
        long Now,
        MultiComponentPool<StatModifierComponent>? StatModifiers = null,
        PackedComponentPool<AbilityScoresComponent>? AbilityScores = null,
        PackedComponentPool<ManaComponent>? Mana = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? HotkeyExpansionUnlocks = null,
        StatusEffectApplierRegistry? StatusEffectAppliers = null,
        PackedComponentPool<DeadComponent>? DeadEntities = null,
        BlueprintRegistry? Definitions = null,
        MultiComponentPool<AuraSourceComponent>? AuraSources = null,
        AuraCatalog? Auras = null,
        EntityBodyParts? BodyParts = null,
        IPlayerQuery? PlayerQuery = null,
        float DurationScaleMultiplier = 1.0f,
        byte ChainDepth = 0,
        FloatingTextFeed? FloatingTextFeed = null) =>
        EffectContext.FromEntity(
            Services(ComponentManager, EntityKeys, EventBus, MathUtility, Health, PlayerQuery, StatusEffectAppliers, StatModifiers, DeadEntities, AbilityScores, Mana, HotkeyExpansionUnlocks, AuraSources, Auras, BodyParts, Definitions, FloatingTextFeed),
            SourceEntityId, TargetEntityId, ActivatorName, ActivatorTags, Now, DurationScaleMultiplier) with { ChainDepth = ChainDepth };

    /// <summary>The services every effect entry works with, over the pools a test names and EmptyPools for the rest.</summary>
    public static EffectServices Services(
        ComponentManager componentManager,
        EntityKeys entityKeys,
        EventBus eventBus,
        MathUtility mathUtility,
        PackedComponentPool<SimpleHealthComponent>? health = null,
        IPlayerQuery? playerQuery = null,
        StatusEffectApplierRegistry? statusEffectAppliers = null,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        PackedComponentPool<ManaComponent>? mana = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        MultiComponentPool<AuraSourceComponent>? auraSources = null,
        AuraCatalog? auras = null,
        EntityBodyParts? bodyParts = null,
        BlueprintRegistry? definitions = null,
        FloatingTextFeed? floatingTextFeed = null)
    {
        var sources = TestAuras.Sources(auraSources, eventBus, auras);
        return new(
            componentManager,
            entityKeys,
            eventBus,
            mathUtility,
            playerQuery ?? TestPlayerQuery.NoPlayer,
            definitions ?? new BlueprintRegistry(),
            health ?? EmptyPools.Packed<SimpleHealthComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            EmptyPools.DamageLedger(deadEntities),
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            abilityScores ?? EmptyPools.Packed<AbilityScoresComponent>(),
            mana ?? EmptyPools.Packed<ManaComponent>(),
            hotkeyExpansionUnlocks ?? EmptyPools.Packed<HotkeyExpansionUnlockComponent>(),
            statusEffectAppliers ?? new StatusEffectApplierRegistry(),
            sources,
            new AuraAnchors(componentManager, new EntityManager(componentManager, initialCapacity: 16), sources, NoAnchorSpawner),
            floatingTextFeed ?? EmptyPools.FloatingTextFeed());
    }

    /// <summary>A test built without an entity factory can't place an aura anchor: one that tries fails loudly rather than placing nothing.</summary>
    private static int NoAnchorSpawner(Engine.Math.Vector3Int tile) =>
        throw new InvalidOperationException($"This test has no entity factory to place an aura anchor at {tile} with; build the modules for that.");
}
