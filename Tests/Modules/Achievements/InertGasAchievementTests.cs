using Game.Blueprints;
﻿using Engine.ECS.Entities;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>
/// Exercises InertGasAchievement end-to-end through the real AchievementModule, mirroring
/// AchievementModuleTests' own Build pattern -- CoreModule is included (not just
/// AchievementModule) because the achievement's own predicate reads NonBlockingComponent off
/// AchievementTriggerContext.ComponentManager, and that pool only exists once CoreModule
/// registers it.
/// </summary>
[TestClass]
public sealed class InertGasAchievementTests
{
    private static readonly Guid InertGasAchievementId = new InertGasAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)), initialEntityCapacity: 10, initialComponentCapacity: 10);

        return (pass.EcsContext, pass.Context.EventBus, pass.World);
    }

    [TestMethod]
    public void PlayerParalyzesPhasingEntity_UnlocksInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var ghostEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(ghostEntityId, new NonBlockingComponent(NonBlockingKind.Phasing));

        eventBus.Publish(new StatusEffectAppliedEvent(ghostEntityId, StatusEffectType.Paralysis, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, playerEntityId, creatures: new BlueprintRegistry())));

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }

    [TestMethod]
    public void PlayerParalyzesBlockingEntity_DoesNotUnlockInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var goblinEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new StatusEffectAppliedEvent(goblinEntityId, StatusEffectType.Paralysis, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, playerEntityId, creatures: new BlueprintRegistry())));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }

    [TestMethod]
    public void PlayerAppliesNonParalysisEffectToPhasingEntity_DoesNotUnlockInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var ghostEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(ghostEntityId, new NonBlockingComponent(NonBlockingKind.Phasing));

        eventBus.Publish(new StatusEffectAppliedEvent(ghostEntityId, StatusEffectType.Poison, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, playerEntityId, creatures: new BlueprintRegistry())));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }

    [TestMethod]
    public void NonPlayerSourceParalyzesPhasingEntity_DoesNotUnlockInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var ghostEntityId = ecsContext.EntityManager.CreateEntity();
        var otherEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(ghostEntityId, new NonBlockingComponent(NonBlockingKind.Phasing));

        eventBus.Publish(new StatusEffectAppliedEvent(ghostEntityId, StatusEffectType.Paralysis, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, otherEntityId, creatures: new BlueprintRegistry())));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }

    [TestMethod]
    public void PlayerParalyzesSelf_DoesNotUnlockInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(playerEntityId, new NonBlockingComponent(NonBlockingKind.Phasing));

        eventBus.Publish(new StatusEffectAppliedEvent(playerEntityId, StatusEffectType.Paralysis, ActionSource.FromEntity(ecsContext.ComponentManager, ecsContext.EntityManager.Keys, playerEntityId, creatures: new BlueprintRegistry())));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }
}
