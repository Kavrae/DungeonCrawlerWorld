using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Tags;
using Game.Modules;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests;

/// <summary>Calls the health helpers for a test that only cares about some of the pools involved, filling in the rest with EmptyPools.</summary>
internal static class TestHealth
{
    public static void Damage(
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        int entityId,
        ushort amount,
        ActionSource source,
        IPlayerQuery? playerQuery,
        string damageType,
        long now,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        EntityBodyParts? bodyParts = null,
        MathUtility? mathUtility = null,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        BodyPartTargetRule? targetRule = null,
        GameplayTagSet damageTags = default,
        BodyPartTargetMode targetMode = BodyPartTargetMode.SingleTarget,
        FloatingTextFeed? floatingTextFeed = null,
        DamageCategory damageCategory = DamageCategory.Direct) =>
        HealthDamage.Apply(health, eventBus, entityId, amount, source, playerQuery ?? TestPlayerQuery.NoPlayer, damageType, now,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            mathUtility,
            deadEntities ?? EmptyPools.Packed<DeadComponent>(),
            floatingTextFeed ?? EmptyPools.FloatingTextFeed(), damageCategory,
            targetRule, damageTags, targetMode);

    public static void Heal(
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        long now,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        EntityBodyParts? bodyParts = null,
        float flatAmount = 0f,
        int? sourceEntityId = null,
        GameplayTagSet activatorTags = default,
        BodyPartTargetMode targetMode = BodyPartTargetMode.All,
        BodyPartTargetRule? targetRule = null,
        MathUtility? mathUtility = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        string healType = "Heal",
        FloatingTextFeed? floatingTextFeed = null,
        HealCategory healCategory = HealCategory.Direct) =>
        HealthHeal.Apply(health, entityId, percentOfMaxHealth, now,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            bodyParts ?? EmptyPools.BodyParts(),
            eventBus ?? new EventBus(), playerQuery ?? TestPlayerQuery.NoPlayer,
            floatingTextFeed ?? EmptyPools.FloatingTextFeed(), healCategory,
            flatAmount, sourceEntityId, activatorTags, targetMode, targetRule, mathUtility, healType);

    public static void HealAllParts(
        EntityBodyParts bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        float flatAmount = 0f,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        int? sourceEntityId = null,
        GameplayTagSet activatorTags = default,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        string healType = "Heal") =>
        ComplexHealthHeal.ApplyToAllParts(bodyParts, health, entityId, percentOfMaxHealth, flatAmount,
            statModifiers ?? EmptyPools.Multi<StatModifierComponent>(),
            eventBus ?? new EventBus(), playerQuery ?? TestPlayerQuery.NoPlayer,
            sourceEntityId, activatorTags, healType);

    public static int PickLowestPercentage(EntityBodyParts bodyParts, int entityId, long now, MultiComponentPool<StatModifierComponent>? statModifiers = null) =>
        BodyPartSelection.PickLowestPercentage(bodyParts, entityId, now, statModifiers ?? EmptyPools.Multi<StatModifierComponent>());
}
