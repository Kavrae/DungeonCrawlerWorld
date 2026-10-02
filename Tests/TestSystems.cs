using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.TerrainContacts.Components;
using Game.Modules.TerrainContacts.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana.Components;
using Game.Modules.Mana.Systems;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.NpcBehavior.Systems;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.World;
using Game.Modules.Actions.Activators;

namespace Tests;

/// <summary>Builds a system for a test that only cares about some of what it requires, filling in the rest with EmptyPools.</summary>
internal static class TestSystems
{
    public static MovementSystem MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        EventBus eventBus,
        IEntityMoveSync entityMoveSync,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<MovementDisabledComponent>? movementDisabled = null) =>
        new(transformComponents, actionLocks, movementComponents, mapQuery, eventBus, entityMoveSync, movedEntities, playerQuery ?? TestPlayerQuery.NoPlayer, processingTiers, processingTierEvents,
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            movementDisabled ?? EmptyPools.Packed<MovementDisabledComponent>());

    public static SimpleHealthRegenSystem SimpleHealthRegenSystem(
        PackedComponentPool<SimpleHealthComponent> healthComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(healthComponents, processingTiers, processingTierEvents,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            abilityScores ?? EmptyPools.Packed<AbilityScoresComponent>(),
            EmptyPools.BodyParts(),
            eventBus ?? new EventBus(), playerQuery ?? TestPlayerQuery.NoPlayer, floatingTextFeed ?? EmptyPools.FloatingTextFeed());

    public static ManaRegenSystem ManaRegenSystem(
        PackedComponentPool<ManaComponent> manaComponents,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null) =>
        new(manaComponents, processingTiers, processingTierEvents,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            abilityScores ?? EmptyPools.Packed<AbilityScoresComponent>());

    public static PoisonSystem PoisonSystem(
        PackedComponentPool<PoisonTimerComponent> timers,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        MathUtility mathUtility,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        EntityBodyParts? bodyParts = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(timers, health, eventBus, playerQuery ?? TestPlayerQuery.NoPlayer, mathUtility,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            EmptyPools.Packed<DeadComponent>(), floatingTextFeed ?? EmptyPools.FloatingTextFeed());

    public static BurningSystem BurningSystem(
        PackedComponentPool<BurningTimerComponent> timers,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        MathUtility mathUtility,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(timers, health, eventBus, playerQuery ?? TestPlayerQuery.NoPlayer, mathUtility,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(), floatingTextFeed ?? EmptyPools.FloatingTextFeed());

    public static BodyPartBurningSystem BodyPartBurningSystem(
        MultiComponentPool<BodyPartBurningTimerComponent> timers,
        EntityBodyParts bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(timers, bodyParts, health, eventBus, playerQuery ?? TestPlayerQuery.NoPlayer,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            deadEntities ?? EmptyPools.Packed<DeadComponent>(), floatingTextFeed ?? EmptyPools.FloatingTextFeed());

    public static TerrainContactSystem TerrainContactSystem(
        TerrainRegistry terrain,
        PackedComponentPool<TerrainContactExposureComponent> exposures,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IMapQuery mapQuery,
        IPlayerQuery? playerQuery,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        MathUtility mathUtility,
        SimulationClock simulationClock,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        EntityBodyParts? bodyParts = null,
        SimulationScope? simulationScope = null,
        FloatingTextFeed? floatingTextFeed = null,
        DirectComponentPool<TransformComponent>? transforms = null,
        StatusEffectApplierRegistry? statusEffectAppliers = null,
        ComponentManager? componentManager = null) =>
        new(terrain, exposures,
            TestActionEffects.Services(componentManager ?? BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16)), new EntityKeys(), eventBus, mathUtility, health, playerQuery,
                statusEffectAppliers, statModifiers, deadEntities, bodyParts: bodyParts, floatingTextFeed: floatingTextFeed),
            mapQuery,
            transforms ?? EmptyPools.Direct<TransformComponent>(),
            movedEntities, simulationClock,
            simulationScope ?? new SimulationScope(static _ => true));

    public static ActionActivationSystem ActionActivationSystem(
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        EntityActions actions,
        PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
        PackedComponentPool<SimpleHealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        EntityKeys entityKeys,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        MultiComponentPool<AuraSourceComponent>? auraSources = null,
        AuraCatalog? auras = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<MeleeDisabledComponent>? meleeDisabled = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null,
        ProcessingTierQuery? processingTiers = null,
        BlueprintRegistry? creatures = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(pendingActivations, actionLocks, actions, pendingDelayedActions,
            TestActionEffects.Services(componentManager, entityKeys, eventBus, mathUtility, health, playerQuery, statusEffectAppliers, statModifiers, deadEntities, abilityScores, mana, hotkeyExpansionUnlocks, auraSources, auras, bodyParts, creatures, floatingTextFeed),
            actionCatalog, mapQuery,
            meleeDisabled ?? EmptyPools.Packed<MeleeDisabledComponent>(),
            dodgingEntities ?? EmptyPools.Packed<DodgingComponent>(),
            processingTiers ?? EmptyPools.Tiers());

    public static DelayedActionSystem DelayedActionSystem(
        PackedComponentPool<PendingDelayedActionComponent> pendingActions,
        EntityActions actions,
        PackedComponentPool<SimpleHealthComponent> health,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        StatusEffectApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        EntityKeys entityKeys,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        MultiComponentPool<AuraSourceComponent>? auraSources = null,
        AuraCatalog? auras = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        EntityBodyParts? bodyParts = null,
        PackedComponentPool<DodgingComponent>? dodgingEntities = null,
        SimulationScope? simulationScope = null,
        ProcessingTierQuery? processingTiers = null,
        ProcessingTierEvents? processingTierEvents = null,
        BlueprintRegistry? creatures = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(pendingActions, actions,
            TestActionEffects.Services(componentManager, entityKeys, eventBus, mathUtility, health, playerQuery, statusEffectAppliers, statModifiers, deadEntities, abilityScores, null, hotkeyExpansionUnlocks, auraSources, auras, bodyParts, creatures, floatingTextFeed),
            actionCatalog, mapQuery,
            dodgingEntities ?? EmptyPools.Packed<DodgingComponent>(),
            processingTiers ?? EmptyPools.Tiers(),
            simulationScope ?? new SimulationScope(static _ => true), processingTierEvents ?? new ProcessingTierEvents());

    public static ConsumableActivationSystem ConsumableActivationSystem(
        PackedComponentPool<PendingConsumableActivationComponent> pendingActivations,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PotionCooldownComponent> potionCooldowns,
        PackedComponentPool<SimpleHealthComponent> health,
        ItemCatalog itemCatalog,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        EventBus eventBus,
        MathUtility mathUtility,
        ComponentManager componentManager,
        EntityKeys entityKeys,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null,
        PackedComponentPool<HotkeyExpansionUnlockComponent>? hotkeyExpansionUnlocks = null,
        PackedComponentPool<AbilityScoresComponent>? abilityScores = null,
        StatusEffectApplierRegistry? statusEffectAppliers = null,
        PackedComponentPool<MeleeDisabledComponent>? meleeDisabled = null,
        IPlayerQuery? playerQuery = null,
        MultiComponentPool<AuraSourceComponent>? auraSources = null,
        AuraCatalog? auras = null,
        MultiComponentPool<ItemHotkeyBindingComponent>? itemHotkeyBindings = null,
        EntityBodyParts? bodyParts = null,
        BlueprintRegistry? creatures = null,
        ProcessingTierQuery? processingTiers = null,
        FloatingTextFeed? floatingTextFeed = null) =>
        new(pendingActivations, actionLocks, potionCooldowns,
            TestActionEffects.Services(componentManager, entityKeys, eventBus, mathUtility, health, playerQuery, statusEffectAppliers, statModifiers, deadEntities, abilityScores, mana, hotkeyExpansionUnlocks, auraSources, auras, bodyParts, creatures, floatingTextFeed),
            itemCatalog, actionCatalog, mapQuery,
            meleeDisabled ?? EmptyPools.Packed<MeleeDisabledComponent>(),
            itemHotkeyBindings ?? EmptyPools.Multi<ItemHotkeyBindingComponent>(),
            processingTiers ?? EmptyPools.Tiers());

    public static TestCombatBehaviorSystem TestCombatBehaviorSystem(
        PackedComponentPool<MovementComponent> movementPool,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        EntityActions actions,
        PackedComponentPool<RaceSlotsComponent> raceSlots,
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<PendingConsumableActivationComponent> pendingConsumableActivations,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<ManaComponent>? mana = null,
        PackedComponentPool<MeleeDisabledComponent>? meleeDisabled = null) =>
        new(movementPool, transformPool, actionLocks, health, bodyParts, inventoryStacks, actions, raceSlots, pendingActivations, pendingConsumableActivations, mapQuery, mathUtility, processingTiers, processingTierEvents,
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            mana ?? EmptyPools.Packed<ManaComponent>(),
            meleeDisabled ?? EmptyPools.Packed<MeleeDisabledComponent>());

    public static DeathSystem DeathSystem(
        PackedComponentPool<DeadComponent> deadEntities,
        MultiComponentPool<NonBlockingComponent> nonBlockingEntities,
        DirectComponentPool<TransformComponent> transforms,
        IEntityMoveSync entityMoveSync,
        IMapQuery mapQuery,
        EventBus eventBus,
        MultiComponentPool<AuraSourceComponent>? auraSources = null) =>
        new(deadEntities, nonBlockingEntities, transforms, entityMoveSync, mapQuery, eventBus,
            auraSources ?? EmptyPools.Multi<AuraSourceComponent>());

    public static BodyPartEffectsSystem BodyPartEffectsSystem(
        EntityBodyParts bodyParts,
        PackedComponentPool<BodyPartStateComponent> bodyPartStates,
        PackedComponentPool<MovementDisabledComponent> movementDisabled,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null) =>
        new(bodyParts, bodyPartStates, movementDisabled, meleeDisabled, processingTiers, processingTierEvents,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>());
}
