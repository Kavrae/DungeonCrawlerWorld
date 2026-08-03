using Engine.Bootstrap;
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
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();

        var module = new AchievementModule();
        module.Configure(new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world });

        IReadOnlyList<IModule> modules = [module, new CoreModule()];
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        return (ecsContext, eventBus, world);
    }

    [TestMethod]
    public void PlayerParalyzesPhasingEntity_UnlocksInertGas()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var ghostEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(ghostEntityId, new NonBlockingComponent(NonBlockingKind.Phasing));

        eventBus.Publish(new StatusEffectApplied(ghostEntityId, StatusEffectType.Paralysis, StatusEffectSource.FromEntity(playerEntityId)));

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

        eventBus.Publish(new StatusEffectApplied(goblinEntityId, StatusEffectType.Paralysis, StatusEffectSource.FromEntity(playerEntityId)));

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

        eventBus.Publish(new StatusEffectApplied(ghostEntityId, StatusEffectType.Poison, StatusEffectSource.FromEntity(playerEntityId)));

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

        eventBus.Publish(new StatusEffectApplied(ghostEntityId, StatusEffectType.Paralysis, StatusEffectSource.FromEntity(otherEntityId)));

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

        eventBus.Publish(new StatusEffectApplied(playerEntityId, StatusEffectType.Paralysis, StatusEffectSource.FromEntity(playerEntityId)));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            InertGasAchievementId));
    }
}
