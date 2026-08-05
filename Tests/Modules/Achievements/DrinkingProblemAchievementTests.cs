using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.World;

namespace Tests.Modules.Achievements;

/// <summary>Exercises DrinkingProblemAchievement end-to-end through the real AchievementModule, mirroring KilledAMobAchievementTests' own Build pattern.</summary>
[TestClass]
public sealed class DrinkingProblemAchievementTests
{
    private static readonly Guid DrinkingProblemAchievementId = new DrinkingProblemAchievement().Id;

    private static (EcsContext EcsContext, EventBus EventBus, Game.World.World World) Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();

        var module = new AchievementModule();
        module.Configure(new GameModuleContext(world, new MathUtility(), eventBus) { PlayerQuery = world });

        IReadOnlyList<IModule> modules = [module];
        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 10, initialComponentCapacity: 10, eventBus);

        return (ecsContext, eventBus, world);
    }

    [TestMethod]
    public void PotionCooldownAbusedForThePlayer_UnlocksDrinkingProblem()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new PotionCooldownAbused(playerEntityId));

        Assert.IsTrue(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            DrinkingProblemAchievementId));
    }

    [TestMethod]
    public void PotionCooldownAbusedForANonPlayerEntity_DoesNotUnlockDrinkingProblem()
    {
        var (ecsContext, eventBus, world) = Build();
        var playerEntityId = ecsContext.EntityManager.CreateEntity();
        var npcEntityId = ecsContext.EntityManager.CreateEntity();
        world.PlayerEntityId = playerEntityId;

        eventBus.Publish(new PotionCooldownAbused(npcEntityId));

        Assert.IsFalse(AchievementQueries.HasEarned(
            ecsContext.ComponentManager.GetMultiPool<AchievementUnlockedComponent>(),
            playerEntityId,
            DrinkingProblemAchievementId));
    }
}
